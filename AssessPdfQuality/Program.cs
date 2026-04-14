/// <summary>
/// Quantitative quality assessment for incoming PDFs before routing.
///
/// Metrics computed per page (no reference image required):
///   - laplacian_variance: Sharpness. Low variance = blurry or noisy scan.
///   - noise_sigma       : Estimated Gaussian noise std dev in background regions.
///   - gradient_mean     : Mean edge strength (Sobel). Low = soft/degraded text edges.
///
/// Routing rule:
///   - laplacian_variance below BLUR_THRESH  → route_to_azure = True  (blurry scan)
///   - noise_sigma above NOISE_THRESH        → route_to_azure = True  (high noise)
///   - Otherwise → route to Gemini Vision
///
/// Usage:
///   AssessPdfQuality test-pdfs/discharge_summary_01.pdf
///   AssessPdfQuality test-pdfs/   (assess all PDFs in a directory)
/// </summary>

using System.Runtime.InteropServices;
using System.Text.Json;
using Docnet.Core;
using Docnet.Core.Models;
using OpenCvSharp;
using SkiaSharp;

// ── Routing thresholds ────────────────────────────────────────────────────────
const double BlurThreshold = 80.0;     // Laplacian variance below this → Azure
const double NoiseThreshold = 6.0;     // Background noise sigma above this → Azure

// Resolution for assessment renders
const int AssessmentDpi = 150;

if (args.Length < 1)
{
    Console.WriteLine("Usage: AssessPdfQuality <pdf_path_or_directory>");
    return 1;
}

var target = args[0];
List<string> pdfFiles;

if (Directory.Exists(target))
{
    pdfFiles = Directory.GetFiles(target, "*.pdf")
        .OrderBy(f => f)
        .ToList();
}
else
{
    pdfFiles = [target];
}

if (pdfFiles.Count == 0)
{
    Console.WriteLine("No PDF files found.");
    return 1;
}

foreach (var pdfPath in pdfFiles)
{
    var report = AssessPdf(pdfPath);
    PrintReport(report);
}

if (args.Contains("--json"))
{
    var allReports = pdfFiles.Select(AssessPdf).ToList();
    Console.WriteLine(JsonSerializer.Serialize(allReports, new JsonSerializerOptions { WriteIndented = true }));
}

return 0;

// ── Per-page metric computation ──────────────────────────────────────────────

static double LaplacianVariance(Mat gray)
{
    using var lap = new Mat();
    Cv2.Laplacian(gray, lap, MatType.CV_64F);
    Cv2.MeanStdDev(lap, out _, out var stddev);
    return stddev[0] * stddev[0]; // variance = stddev^2
}

static double NoiseSigma(Mat gray)
{
    using var blurred = new Mat();
    Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0);

    using var residual = new Mat();
    gray.ConvertTo(residual, MatType.CV_32F);
    using var blurredF = new Mat();
    blurred.ConvertTo(blurredF, MatType.CV_32F);
    using var diff = residual - blurredF;

    // Background pixels (near-white, > 210)
    using var mask = new Mat();
    Cv2.Threshold(gray, mask, 210, 255, ThresholdTypes.Binary);

    var bgCount = Cv2.CountNonZero(mask);
    if (bgCount < 200)
    {
        Cv2.MeanStdDev(diff, out _, out var fullStd);
        return fullStd[0];
    }

    Cv2.MeanStdDev(diff, out _, out var stddev, mask);
    return stddev[0];
}

static double GradientMean(Mat gray)
{
    using var gx = new Mat();
    using var gy = new Mat();
    Cv2.Sobel(gray, gx, MatType.CV_64F, 1, 0, ksize: 3);
    Cv2.Sobel(gray, gy, MatType.CV_64F, 0, 1, ksize: 3);

    using var gxSq = new Mat();
    using var gySq = new Mat();
    Cv2.Multiply(gx, gx, gxSq);
    Cv2.Multiply(gy, gy, gySq);

    using var magnitude = new Mat();
    Cv2.Sqrt(gxSq + gySq, magnitude);

    return Cv2.Mean(magnitude)[0];
}

static PageQualityMetrics AssessPage(Mat imgBgr, int pageNumber)
{
    using var gray = new Mat();
    Cv2.CvtColor(imgBgr, gray, ColorConversionCodes.BGR2GRAY);

    var lv = LaplacianVariance(gray);
    var ns = NoiseSigma(gray);
    var gm = GradientMean(gray);

    var lowQuality = lv < BlurThreshold || ns > NoiseThreshold;

    return new PageQualityMetrics
    {
        PageNumber = pageNumber,
        LaplacianVariance = Math.Round(lv, 3),
        NoiseSigma = Math.Round(ns, 4),
        GradientMean = Math.Round(gm, 3),
        IsLowQuality = lowQuality,
    };
}

// ── PDF-level assessment ─────────────────────────────────────────────────────

static PdfQualityReport AssessPdf(string pdfPath)
{
    var report = new PdfQualityReport
    {
        PdfPath = pdfPath,
    };

    using var docReader = DocLib.Instance.GetDocReader(pdfPath, new PageDimensions(1240, 1754)); // ~A4 at 150 DPI
    var pageCount = docReader.GetPageCount();
    report.PageCount = pageCount;

    for (int i = 0; i < pageCount; i++)
    {
        using var pageReader = docReader.GetPageReader(i);
        var rawBytes = pageReader.GetImage();
        var width = pageReader.GetPageWidth();
        var height = pageReader.GetPageHeight();

        // Docnet returns BGRA8888
        using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        Marshal.Copy(rawBytes, 0, bitmap.GetPixels(), rawBytes.Length);

        // Convert to OpenCV Mat (BGR)
        using var bgraMat = new Mat(height, width, MatType.CV_8UC4);
        Marshal.Copy(rawBytes, 0, bgraMat.Data, rawBytes.Length);
        using var bgrMat = new Mat();
        Cv2.CvtColor(bgraMat, bgrMat, ColorConversionCodes.BGRA2BGR);

        var metrics = AssessPage(bgrMat, i);
        report.Pages.Add(metrics);

        if (metrics.LaplacianVariance < BlurThreshold)
        {
            report.RouteToAzure = true;
            report.Reasons.Add($"page {i}: laplacian_variance {metrics.LaplacianVariance:F2} < {BlurThreshold} (blurry)");
        }

        if (metrics.NoiseSigma > NoiseThreshold)
        {
            report.RouteToAzure = true;
            report.Reasons.Add($"page {i}: noise_sigma {metrics.NoiseSigma:F4} > {NoiseThreshold} (noisy scan)");
        }
    }

    if (!report.RouteToAzure)
        report.Reasons.Add("All metrics within acceptable thresholds - Gemini Vision path.");

    return report;
}

// ── CLI display ──────────────────────────────────────────────────────────────

static void PrintReport(PdfQualityReport report)
{
    Console.WriteLine();
    Console.WriteLine(new string('=', 60));
    Console.WriteLine($"  {Path.GetFileName(report.PdfPath)}");
    Console.WriteLine(new string('=', 60));
    Console.WriteLine($"  Pages       : {report.PageCount}");
    Console.WriteLine($"  Route       : {(report.RouteToAzure ? "-> AZURE OCR" : "-> GEMINI VISION")}");
    foreach (var r in report.Reasons)
        Console.WriteLine($"    * {r}");
    Console.WriteLine();
    Console.WriteLine($"  {"Page",-6} {"Laplacian",12} {"Noise s",10} {"Gradient",10} {"LowQ?",6}");
    Console.WriteLine($"  {new string('-', 6)} {new string('-', 12)} {new string('-', 10)} {new string('-', 10)} {new string('-', 6)}");
    foreach (var p in report.Pages)
    {
        var flag = p.IsLowQuality ? "YES" : "";
        Console.WriteLine($"  {p.PageNumber,-6} {p.LaplacianVariance,12:F2} {p.NoiseSigma,10:F4} {p.GradientMean,10:F2} {flag,6}");
    }
    Console.WriteLine();
}

// ── Data models ──────────────────────────────────────────────────────────────

record PageQualityMetrics
{
    public int PageNumber { get; init; }
    public double LaplacianVariance { get; init; }
    public double NoiseSigma { get; init; }
    public double GradientMean { get; init; }
    public bool IsLowQuality { get; init; }
}

record PdfQualityReport
{
    public string PdfPath { get; init; } = "";
    public int PageCount { get; set; }
    public bool RouteToAzure { get; set; }
    public List<PageQualityMetrics> Pages { get; } = [];
    public List<string> Reasons { get; } = [];
}
