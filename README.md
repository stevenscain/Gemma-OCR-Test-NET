# Gemma OCR Test (.NET)

> **Quick prototype** — tested locally with [Gemma 4 (e4b)](https://ollama.com/library/gemma4:e4b) running on Ollama. This is an exploratory spike, not production code.

An end-to-end OCR accuracy testing suite built entirely in .NET. Generates synthetic medical PDFs with known ground-truth text, degrades them to simulate real-world scan quality, runs OCR via a local Ollama vision model, and reports accuracy metrics.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Ollama](https://ollama.com/) running locally with the `gemma4:e4b` model pulled
- Windows (OpenCvSharp4 runtime is Windows-only as configured)

```bash
ollama pull gemma4:e4b
```

## Solution Structure

```
Gemma-OCR-Test-NET.sln
├── GenerateMedicalPdfs/    # Synthetic medical PDF generator
├── DegradePdfs/            # Quality degradation pipeline (noise, blur, skew, etc.)
├── AssessPdfQuality/       # Per-page quality metrics & routing logic
├── GemmaOcrTest/           # OCR engine — sends PDF pages to Ollama
├── RunTests/               # Test orchestrator — batch OCR + markdown reports
├── test-pdfs/              # 17 generated clean PDFs (+ optional .txt ground truth)
└── test-pdfs-degraded/     # 88 degraded PDFs (22 degradations × 4 source PDFs)
```

## Quick Start

```bash
# 1. Build everything
dotnet build

# 2. Generate synthetic medical PDFs
cd GenerateMedicalPdfs
dotnet run

# 3. (Optional) Generate ground-truth text files alongside PDFs
dotnet run -- --ground-truth

# 4. Generate degraded versions for quality testing
cd ../DegradePdfs
dotnet run

# 5. Assess quality metrics on any PDF or directory
cd ../AssessPdfQuality
dotnet run -- ../test-pdfs/
dotnet run -- ../test-pdfs-degraded/

# 6. Run full OCR test suite (requires Ollama running)
cd ../RunTests
dotnet run
```

## Projects

### GenerateMedicalPdfs

Generates 17 synthetic medical PDFs using QuestPDF and Bogus:

- **Discharge summaries** (single & multi-page) — full hospital narratives with vitals, diagnoses, medications, consultation notes, and follow-up instructions
- **Lab reports** (single & multi-page) — CBC, CMP, lipid, thyroid, and A1c panels with tabulated results, reference ranges, and flags
- **Prescriptions** — Rx documents with DEA/NPI numbers, dosing instructions, and refill info
- **Commingled stress tests** (1-page, 2-page, 3-page intervals) — 6 patients per document with sensitive content deliberately placed at page boundaries to test chunk-based processing

All patient data is localized to **18 East Coast US cities** with real zip codes, area codes, and street names.

**Sensitive content categories** (placed at page boundaries in commingled tests):
- Substance abuse & addiction assessments (42 CFR Part 2)
- Psychiatric evaluations & mental health notes
- HIV/STI screening & treatment
- Genetic testing results (GINA-protected)
- Domestic violence / IPV screening

**Ground truth flag:** `--ground-truth` outputs a `.txt` file alongside each PDF containing the exact source text used to generate the document, line by line. This enables character-level accuracy comparison against OCR output.

| Package | Version | Purpose |
|---------|---------|---------|
| QuestPDF | 2026.2.4 | PDF generation (Community license) |
| Bogus | 35.6.5 | Synthetic patient data |

### DegradePdfs

Renders 4 representative source PDFs to images, applies 22 quality degradations, and reassembles into PDFs. Produces 88 degraded PDFs total.

**Degradation types:**

| Category | Variants |
|----------|----------|
| Gaussian noise | light (σ=10), medium (σ=25), heavy (σ=50) |
| Gaussian blur | light (k=3), medium (k=5), heavy (k=9) |
| Salt & pepper noise | light (0.2%), medium (1%), heavy (3%) |
| Contrast | low (α=0.4), faded (α=0.6), high (α=1.8) |
| JPEG compression | q30, q10, q5 |
| Rotation/skew | 1°, 3°, 5° |
| Uneven lighting | gradient overlay simulating scanner lid gap |
| Combined scan | light, medium, heavy (noise + blur + JPEG + skew) |

| Package | Version | Purpose |
|---------|---------|---------|
| Docnet.Core | 2.6.0 | PDF page rendering to BGRA pixels |
| SkiaSharp | 3.116.1 | Image compositing and PNG encoding |
| OpenCvSharp4 | 4.13.0.20260330 | Image degradation operations |
| QuestPDF | 2026.2.4 | Reassemble degraded images into PDFs |

### AssessPdfQuality

Computes per-page quality metrics to determine OCR routing. No reference image required.

**Metrics:**
- **Laplacian variance** — sharpness indicator. Low values = blurry or noisy scan.
- **Noise sigma** — estimated Gaussian noise standard deviation in background (near-white) regions.
- **Gradient mean** — mean Sobel edge strength. Low values = soft/degraded text edges.

**Routing logic:**
- Laplacian variance < 80 → route to Azure OCR (blurry)
- Noise sigma > 6.0 → route to Azure OCR (noisy)
- Otherwise → route to Gemini Vision

```bash
# Single file
dotnet run -- ../test-pdfs/discharge_summary_01.pdf

# Whole directory
dotnet run -- ../test-pdfs-degraded/

# JSON output
dotnet run -- ../test-pdfs/ --json
```

| Package | Version | Purpose |
|---------|---------|---------|
| Docnet.Core | 2.6.0 | PDF rendering |
| OpenCvSharp4 | 4.13.0.20260330 | Laplacian, Sobel, noise estimation |
| SkiaSharp | 3.116.1 | BGRA pixel handling |

### GemmaOcrTest

Sends a PDF (or image) to a local Ollama instance running `gemma4:e4b` for OCR extraction.

```bash
# OCR a PDF
dotnet run -- ../test-pdfs/discharge_summary_01.pdf

# OCR with a custom prompt
dotnet run -- ../test-pdfs/lab_report_01.pdf "Extract all lab values as a table"
```

For multi-page PDFs, each page is rendered at 200 DPI (1700×2200 for Letter), sent individually, and output is separated by `=== PAGE N ===` markers.

| Package | Version | Purpose |
|---------|---------|---------|
| Docnet.Core | 2.6.0 | PDF to PNG rendering |
| SkiaSharp | 3.116.1 | BGRA to PNG conversion |

### RunTests

Batch orchestrator that runs `GemmaOcrTest` against every PDF in `test-pdfs/`, captures output, and generates a timestamped markdown report in `test-results/`.

Reports include: pass/fail status, page boundary detection accuracy, timing, and extracted text previews.

| Package | Version | Purpose |
|---------|---------|---------|
| Docnet.Core | 2.6.0 | Page count verification |

## Implementation Details

### Docnet BGRA Transparent Background

Docnet.Core renders PDF pages as BGRA8888 with **alpha = 0** (fully transparent background). If treated as opaque, transparent pixels appear black, producing all-black page images.

**Fix:** Composite the Docnet output onto a white `SKBitmap` canvas:

```csharp
using var srcBitmap = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
Marshal.Copy(rawBytes, 0, srcBitmap.GetPixels(), rawBytes.Length);

using var opaque = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque);
using var canvas = new SKCanvas(opaque);
canvas.Clear(SKColors.White);
canvas.DrawBitmap(srcBitmap, 0, 0);
```

This is applied in both `DegradePdfs` and `GemmaOcrTest`.

### Gaussian Noise in Float Space

Adding Gaussian noise directly in `CV_8UC3` (byte) space produces black artifacts because negative noise values wrap around to 255. 

**Fix:** Convert to `CV_32FC3` (float), add noise, then convert back — `ConvertTo` auto-clamps to 0–255:

```csharp
src.ConvertTo(srcF, MatType.CV_32FC3);
Cv2.Randn(noise, Scalar.All(0), Scalar.All(sigma));
Cv2.Add(srcF, noise, sumF);
sumF.ConvertTo(dst, MatType.CV_8UC3);
```

### OpenCvSharp Size Ambiguity

Both `OpenCvSharp.Size` and `QuestPDF.Infrastructure.Size` exist. In files that reference both namespaces, use fully qualified `new OpenCvSharp.Size(...)`.

### Ground Truth Text Capture

The `GT()` helper function records text while returning it unmodified, so `col.Item().Text(GT("..."))` both renders to PDF and captures the string. When `--ground-truth` is passed, `_gt` is initialized as a `List<string>` and each text string is appended. After each PDF is generated, the list is written to a `.txt` file with the same base name.

## Notes on ABCpdf for Quality Detection

[ABCpdf .NET](https://www.websupergoo.com/abcpdf-1.htm) (by WebSupergoo, $329/license) was evaluated as a potential alternative for PDF quality detection. Findings:

**What ABCpdf can do:**
- Image Placement Analysis & Extraction — pull embedded images from PDFs for downstream analysis
- Built-in image effects — Histogram, Laplacian, Autolevels, Despeckle — could provide some signal about image characteristics
- Rendering resolution control — rasterize pages at specific DPI and color depths
- PDF/A validation — structural conformance checks (not visual quality)
- Low-level content stream inspection — enumerate all objects, images, fonts, and compression types within a PDF

**What ABCpdf cannot do (for this use case):**
- No built-in quality score or scan quality assessment
- No blur detection, noise estimation, or sharpness metrics
- No automated preflight for OCR-readiness (contrast ratio, skew detection, resolution adequacy)

**Conclusion:** ABCpdf could replace Docnet for the image extraction step (rendering pages to raster images), but the actual quality analysis — Laplacian variance, noise sigma, gradient mean — still requires a computer vision library like OpenCvSharp. ABCpdf is only worth considering if you also need its other capabilities (PDF/A conformance, HTML-to-PDF, document manipulation).
