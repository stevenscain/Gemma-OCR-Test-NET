/// <summary>
/// OCR Test Runner - Processes all test PDFs through Gemma 4 and produces a summary report.
///
/// Runs each PDF through the GemmaOcrTest tool, captures OCR output with page boundaries,
/// and generates a results summary showing accuracy metrics and extracted text.
/// </summary>

using System.Diagnostics;

var testPdfDir = Path.Combine("..", "test-pdfs");
var resultsDir = Path.Combine("..", "test-results");
var gemmaExe = Path.Combine("..", "GemmaOcrTest", "bin", "Debug", "net8.0", "GemmaOcrTest.exe");

if (!File.Exists(gemmaExe))
{
    Console.Error.WriteLine($"ERROR: {gemmaExe} not found. Build the project first:");
    Console.Error.WriteLine("  cd GemmaOcrTest && dotnet build");
    return 1;
}

if (!Directory.Exists(testPdfDir))
{
    Console.Error.WriteLine($"ERROR: {testPdfDir}/ not found. Run GenerateMedicalPdfs first.");
    return 1;
}

var pdfs = Directory.GetFiles(testPdfDir, "*.pdf").OrderBy(f => f).ToList();
if (pdfs.Count == 0)
{
    Console.Error.WriteLine("No PDFs found in test-pdfs/");
    return 1;
}

Console.WriteLine($"Found {pdfs.Count} test PDFs. Starting OCR processing...\n");

var results = new List<TestResult>();

for (int i = 0; i < pdfs.Count; i++)
{
    var pdfPath = pdfs[i];
    var pdfName = Path.GetFileName(pdfPath);
    var actualPages = GetPdfPageCount(pdfPath);
    Console.Write($"[{i + 1}/{pdfs.Count}] {pdfName} ({actualPages} page(s))... ");

    var ocr = RunOcr(pdfPath);
    var extractedPages = ParsePageBoundaries(ocr.Stdout);

    Console.WriteLine($"done in {ocr.ElapsedSeconds}s - {extractedPages.Count} page(s) extracted");

    results.Add(new TestResult
    {
        Filename = pdfName,
        ActualPages = actualPages,
        ExtractedPages = extractedPages,
        ExitCode = ocr.ExitCode,
        ElapsedSeconds = ocr.ElapsedSeconds,
        StderrInfo = ocr.Stderr.Trim(),
        RawStdout = ocr.Stdout,
    });
}

var (reportPath, _) = GenerateReport(results);

// Print summary
Console.WriteLine();
Console.WriteLine(new string('=', 60));
Console.WriteLine("TEST RESULTS SUMMARY");
Console.WriteLine(new string('=', 60));
var passed = results.Count(r => r.ExitCode == 0 && r.ExtractedPages.Count > 0);
var failed = results.Count - passed;
var totalTime = results.Sum(r => r.ElapsedSeconds);
Console.WriteLine($"  Passed:     {passed}/{results.Count}");
Console.WriteLine($"  Failed:     {failed}/{results.Count}");
Console.WriteLine($"  Total time: {totalTime:F1}s");
Console.WriteLine();

// Page boundary detection
var multipage = results.Where(r => r.ActualPages > 1).ToList();
if (multipage.Count > 0)
{
    Console.WriteLine("MULTI-PAGE BOUNDARY DETECTION:");
    foreach (var r in multipage)
    {
        var detected = r.ExtractedPages.Count;
        var match = detected == r.ActualPages ? "OK" : "MISMATCH";
        Console.WriteLine($"  {r.Filename}: {detected}/{r.ActualPages} pages [{match}]");
    }
    Console.WriteLine();
}

// Brief text preview
Console.WriteLine("EXTRACTED TEXT PREVIEW:");
foreach (var r in results)
{
    foreach (var (pageNum, text) in r.ExtractedPages.OrderBy(kv => kv.Key))
    {
        var label = r.Filename;
        if (r.ExtractedPages.Count > 1) label += $" p{pageNum}";
        var preview = text[..Math.Min(120, text.Length)].Replace("\n", " ");
        Console.WriteLine($"  {label}: {preview}...");
    }
}
Console.WriteLine();
Console.WriteLine($"Full report saved to: {reportPath}");

return 0;

// ── Helper methods ───────────────────────────────────────────────────────────

static int GetPdfPageCount(string pdfPath)
{
    try
    {
        using var docReader = Docnet.Core.DocLib.Instance.GetDocReader(
            pdfPath, new Docnet.Core.Models.PageDimensions(100, 100));
        return docReader.GetPageCount();
    }
    catch
    {
        return 0;
    }
}

OcrResult RunOcr(string pdfPath)
{
    var sw = Stopwatch.StartNew();
    try
    {
        var psi = new ProcessStartInfo(gemmaExe, $"\"{pdfPath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        var timedOut = !process.WaitForExit(600_000); // 10 min timeout
        sw.Stop();

        if (timedOut)
        {
            try { process.Kill(); } catch { }
            return new OcrResult("", "TIMEOUT after 600s", -1, 600.0);
        }

        return new OcrResult(stdout, stderr, process.ExitCode, Math.Round(sw.Elapsed.TotalSeconds, 1));
    }
    catch (Exception ex)
    {
        sw.Stop();
        return new OcrResult("", $"Error: {ex.Message}", -1, Math.Round(sw.Elapsed.TotalSeconds, 1));
    }
}

static Dictionary<string, string> ParsePageBoundaries(string stdout)
{
    var pages = new Dictionary<string, string>();
    string? currentPage = null;
    var currentText = new List<string>();

    foreach (var line in stdout.Split('\n'))
    {
        var stripped = line.Trim();
        if (stripped.StartsWith("=== PAGE ") && stripped.EndsWith("==="))
        {
            if (currentPage != null)
                pages[currentPage] = string.Join("\n", currentText).Trim();
            currentPage = stripped.Replace("=== PAGE ", "").Replace(" ===", "");
            currentText.Clear();
        }
        else
        {
            currentText.Add(line);
        }
    }

    if (currentPage != null)
        pages[currentPage] = string.Join("\n", currentText).Trim();
    else if (!string.IsNullOrWhiteSpace(stdout))
        pages["1"] = stdout.Trim();

    return pages;
}

(string Path, string Content) GenerateReport(List<TestResult> results)
{
    var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
    Directory.CreateDirectory(resultsDir);
    var reportPath = System.IO.Path.Combine(resultsDir, $"ocr_test_report_{timestamp}.md");

    var lines = new List<string>
    {
        "# Gemma 4 OCR Test Report",
        $"**Generated:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
        $"**Total PDFs tested:** {results.Count}",
        "",
        "## Summary",
        "",
        "| PDF File | Pages | Pages Detected | Time (s) | Status |",
        "|----------|-------|----------------|----------|--------|",
    };

    int totalPassed = 0, totalFailed = 0;
    double totalTime = 0;

    foreach (var r in results)
    {
        totalTime += r.ElapsedSeconds;
        var detected = r.ExtractedPages.Count;
        var status = r.ExitCode == 0 && detected > 0 ? "PASS" : "FAIL";
        if (status == "PASS") totalPassed++; else totalFailed++;

        var pageMatch = detected == r.ActualPages
            ? $"{detected}/{r.ActualPages}"
            : $"**{detected}/{r.ActualPages}**";
        lines.Add($"| {r.Filename} | {r.ActualPages} | {pageMatch} | {r.ElapsedSeconds} | {status} |");
    }

    lines.Add("");
    lines.Add($"**Passed:** {totalPassed} | **Failed:** {totalFailed} | **Total time:** {totalTime:F1}s");
    lines.Add("");

    // Multi-page boundary detection
    var multipageResults = results.Where(r => r.ActualPages > 1).ToList();
    if (multipageResults.Count > 0)
    {
        lines.Add("## Multi-Page Boundary Detection");
        lines.Add("");
        foreach (var r in multipageResults)
        {
            var detected = r.ExtractedPages.Count;
            lines.Add($"### {r.Filename} ({r.ActualPages} pages)");
            lines.Add(detected == r.ActualPages
                ? $"All {r.ActualPages} page boundaries correctly detected."
                : $"**Mismatch:** Expected {r.ActualPages} pages, detected {detected}.");
            lines.Add("");
            foreach (var (pageNum, text) in r.ExtractedPages.OrderBy(kv => kv.Key))
            {
                var preview = text[..Math.Min(200, text.Length)].Replace("\n", " ");
                if (text.Length > 200) preview += "...";
                lines.Add($"- **Page {pageNum}**: {text.Length} chars - `{preview}`");
            }
            lines.Add("");
        }
    }

    // Detailed results
    lines.Add("## Detailed OCR Output");
    lines.Add("");
    foreach (var r in results)
    {
        lines.Add($"### {r.Filename}");
        lines.Add($"- **Pages:** {r.ActualPages}");
        lines.Add($"- **Time:** {r.ElapsedSeconds}s");
        lines.Add($"- **Exit code:** {r.ExitCode}");
        if (!string.IsNullOrEmpty(r.StderrInfo))
            lines.Add($"- **Stderr:** `{r.StderrInfo[..Math.Min(200, r.StderrInfo.Length)]}`");
        lines.Add("");

        if (r.ExtractedPages.Count > 0)
        {
            foreach (var (pageNum, text) in r.ExtractedPages.OrderBy(kv => kv.Key))
            {
                if (r.ExtractedPages.Count > 1) lines.Add($"#### Page {pageNum}");
                lines.Add("```");
                lines.Add(text.Length > 2000 ? text[..2000] : text);
                if (text.Length > 2000) lines.Add($"\n... (truncated, {text.Length} chars total)");
                lines.Add("```");
                lines.Add("");
            }
        }
        else
        {
            lines.Add("*No text extracted.*");
            lines.Add("");
        }
        lines.Add("---");
        lines.Add("");
    }

    var content = string.Join("\n", lines);
    File.WriteAllText(reportPath, content);
    return (reportPath, content);
}

// ── Models ───────────────────────────────────────────────────────────────────

record OcrResult(string Stdout, string Stderr, int ExitCode, double ElapsedSeconds);

record TestResult
{
    public string Filename { get; init; } = "";
    public int ActualPages { get; init; }
    public Dictionary<string, string> ExtractedPages { get; init; } = [];
    public int ExitCode { get; init; }
    public double ElapsedSeconds { get; init; }
    public string StderrInfo { get; init; } = "";
    public string RawStdout { get; init; } = "";
}
