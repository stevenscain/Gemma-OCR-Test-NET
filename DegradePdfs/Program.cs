using Docnet.Core;
using Docnet.Core.Models;
using OpenCvSharp;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SkiaSharp;

QuestPDF.Settings.License = LicenseType.Community;

var inputDir = Path.Combine("..", "test-pdfs");
var outputDir = Path.Combine("..", "test-pdfs-degraded");
Directory.CreateDirectory(outputDir);

if (!Directory.Exists(inputDir) || Directory.GetFiles(inputDir, "*.pdf").Length == 0)
{
    Console.Error.WriteLine($"No PDFs found in '{inputDir}'. Run GenerateMedicalPdfs first.");
    return 1;
}

// Pick a representative subset of source PDFs to degrade
var sourcePdfs = new[]
{
    "discharge_summary_01.pdf",
    "lab_report_01.pdf",
    "discharge_summary_multi_01.pdf",
    "commingled_boundary_2pg.pdf",
};

var degradations = new (string Name, Func<Mat, Mat> Apply)[]
{
    ("gaussian_noise_light",   img => AddGaussianNoise(img, sigma: 10)),
    ("gaussian_noise_medium",  img => AddGaussianNoise(img, sigma: 25)),
    ("gaussian_noise_heavy",   img => AddGaussianNoise(img, sigma: 50)),
    ("blur_light",             img => ApplyBlur(img, ksize: 3)),
    ("blur_medium",            img => ApplyBlur(img, ksize: 5)),
    ("blur_heavy",             img => ApplyBlur(img, ksize: 9)),
    ("salt_pepper_light",      img => AddSaltAndPepper(img, density: 0.002)),
    ("salt_pepper_medium",     img => AddSaltAndPepper(img, density: 0.01)),
    ("salt_pepper_heavy",      img => AddSaltAndPepper(img, density: 0.03)),
    ("low_contrast",           img => AdjustContrast(img, alpha: 0.4, beta: 80)),
    ("faded",                  img => AdjustContrast(img, alpha: 0.6, beta: 100)),
    ("high_contrast",          img => AdjustContrast(img, alpha: 1.8, beta: -50)),
    ("jpeg_q30",               img => JpegCompress(img, quality: 30)),
    ("jpeg_q10",               img => JpegCompress(img, quality: 10)),
    ("jpeg_q5",                img => JpegCompress(img, quality: 5)),
    ("skew_1deg",              img => ApplySkew(img, angleDeg: 1.0)),
    ("skew_3deg",              img => ApplySkew(img, angleDeg: 3.0)),
    ("skew_5deg",              img => ApplySkew(img, angleDeg: 5.0)),
    ("uneven_lighting",        img => ApplyUnevenLighting(img)),
    ("combined_scan_light",    img => CombinedScan(img, noiseSigma: 8,  blurK: 3, jpegQ: 50, skewDeg: 0.5)),
    ("combined_scan_medium",   img => CombinedScan(img, noiseSigma: 20, blurK: 5, jpegQ: 30, skewDeg: 1.5)),
    ("combined_scan_heavy",    img => CombinedScan(img, noiseSigma: 40, blurK: 7, jpegQ: 15, skewDeg: 3.0)),
};

var total = 0;
foreach (var pdfName in sourcePdfs)
{
    var inputPath = Path.Combine(inputDir, pdfName);
    if (!File.Exists(inputPath))
    {
        Console.WriteLine($"Skipping (not found): {pdfName}");
        continue;
    }

    var baseName = Path.GetFileNameWithoutExtension(pdfName);
    Console.WriteLine($"\nProcessing: {pdfName}");

    // Render all pages to images once
    var pageImages = RenderPdfToImages(inputPath);
    Console.WriteLine($"  Rendered {pageImages.Count} page(s)");

    foreach (var (degradName, applyFn) in degradations)
    {
        var outName = $"{baseName}__{degradName}.pdf";
        var outPath = Path.Combine(outputDir, outName);

        var degradedImages = new List<byte[]>();
        foreach (var pageImg in pageImages)
        {
            using var mat = Mat.FromImageData(pageImg, ImreadModes.Color);
            using var degraded = applyFn(mat);
            degradedImages.Add(MatToPng(degraded));
        }

        SaveImagesToPdf(degradedImages, outPath);
        Console.WriteLine($"  Generated: {outName}");
        total++;
    }

    // Free rendered pages
    pageImages.Clear();
}

Console.WriteLine($"\nDone! Generated {total} degraded PDFs in '{outputDir}/'");
return 0;

// ── PDF rendering ────────────────────────────────────────────────────────────

static List<byte[]> RenderPdfToImages(string pdfPath)
{
    var pages = new List<byte[]>();
    using var docReader = DocLib.Instance.GetDocReader(pdfPath, new PageDimensions(1700, 2200));
    for (int i = 0; i < docReader.GetPageCount(); i++)
    {
        using var pageReader = docReader.GetPageReader(i);
        var rawBytes = pageReader.GetImage();
        var w = pageReader.GetPageWidth();
        var h = pageReader.GetPageHeight();

        // Docnet renders BGRA with transparent background — composite onto white
        using var srcBitmap = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        var ptr = srcBitmap.GetPixels();
        System.Runtime.InteropServices.Marshal.Copy(rawBytes, 0, ptr, rawBytes.Length);

        using var opaque = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(opaque);
        canvas.Clear(SKColors.White);
        canvas.DrawBitmap(srcBitmap, 0, 0);
        canvas.Flush();

        using var image = SKImage.FromBitmap(opaque);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        pages.Add(data.ToArray());
    }
    return pages;
}

static byte[] MatToPng(Mat mat)
{
    Cv2.ImEncode(".png", mat, out var buf);
    return buf;
}

static void SaveImagesToPdf(List<byte[]> pageImages, string outPath)
{
    Document.Create(container =>
    {
        foreach (var imgBytes in pageImages)
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(0);
                page.Content().Image(imgBytes).FitArea();
            });
        }
    }).GeneratePdf(outPath);
}

// ── Degradation functions ────────────────────────────────────────────────────

static Mat AddGaussianNoise(Mat src, double sigma)
{
    // Work in float space so negative noise values are handled correctly
    var srcF = new Mat();
    src.ConvertTo(srcF, MatType.CV_32FC3);

    var noise = new Mat(src.Size(), MatType.CV_32FC3);
    Cv2.Randn(noise, Scalar.All(0), Scalar.All(sigma));

    var sumF = new Mat();
    Cv2.Add(srcF, noise, sumF);
    srcF.Dispose();
    noise.Dispose();

    var dst = new Mat();
    sumF.ConvertTo(dst, MatType.CV_8UC3); // clamps to 0-255
    sumF.Dispose();
    return dst;
}

static Mat ApplyBlur(Mat src, int ksize)
{
    var dst = new Mat();
    Cv2.GaussianBlur(src, dst, new OpenCvSharp.Size(ksize, ksize), 0);
    return dst;
}

static Mat AddSaltAndPepper(Mat src, double density)
{
    var dst = src.Clone();
    var rng = new Random(42);
    var totalPixels = src.Rows * src.Cols;
    var numSalt = (int)(totalPixels * density);
    var numPepper = (int)(totalPixels * density);

    // Salt (white)
    for (int i = 0; i < numSalt; i++)
    {
        var y = rng.Next(src.Rows);
        var x = rng.Next(src.Cols);
        dst.Set(y, x, new Vec3b(255, 255, 255));
    }
    // Pepper (black)
    for (int i = 0; i < numPepper; i++)
    {
        var y = rng.Next(src.Rows);
        var x = rng.Next(src.Cols);
        dst.Set(y, x, new Vec3b(0, 0, 0));
    }
    return dst;
}

static Mat AdjustContrast(Mat src, double alpha, double beta)
{
    var dst = new Mat();
    src.ConvertTo(dst, -1, alpha, beta);
    return dst;
}

static Mat JpegCompress(Mat src, int quality)
{
    var param = new ImageEncodingParam(ImwriteFlags.JpegQuality, quality);
    Cv2.ImEncode(".jpg", src, out var buf, param);
    return Cv2.ImDecode(buf, ImreadModes.Color);
}

static Mat ApplySkew(Mat src, double angleDeg)
{
    var center = new Point2f(src.Cols / 2f, src.Rows / 2f);
    using var rotMat = Cv2.GetRotationMatrix2D(center, angleDeg, 1.0);
    var dst = new Mat();
    Cv2.WarpAffine(src, dst, rotMat, src.Size(), InterpolationFlags.Linear,
        BorderTypes.Constant, new Scalar(255, 255, 255));
    return dst;
}

static Mat ApplyUnevenLighting(Mat src)
{
    // Create a gradient that darkens one corner and brightens the opposite
    var gradient = new Mat(src.Size(), MatType.CV_8UC1);
    for (int y = 0; y < src.Rows; y++)
    {
        for (int x = 0; x < src.Cols; x++)
        {
            var val = (byte)Math.Clamp(
                128 + (int)(80.0 * x / src.Cols) - (int)(60.0 * y / src.Rows), 0, 255);
            gradient.Set(y, x, val);
        }
    }

    // Convert gradient to 3-channel
    var gradient3 = new Mat();
    Cv2.CvtColor(gradient, gradient3, ColorConversionCodes.GRAY2BGR);
    gradient.Dispose();

    // Blend using overlay-style: multiply then normalize
    var srcF = new Mat();
    var gradF = new Mat();
    var result = new Mat();
    src.ConvertTo(srcF, MatType.CV_32FC3, 1.0 / 255.0);
    gradient3.ConvertTo(gradF, MatType.CV_32FC3, 1.0 / 255.0);
    gradient3.Dispose();

    Cv2.Multiply(srcF, gradF, result, 2.0);
    srcF.Dispose();
    gradF.Dispose();

    var dst = new Mat();
    result.ConvertTo(dst, MatType.CV_8UC3, 255.0);
    result.Dispose();

    return dst;
}

static Mat CombinedScan(Mat src, double noiseSigma, int blurK, int jpegQ, double skewDeg)
{
    // Chain: skew -> blur -> noise -> jpeg compression
    using var skewed = ApplySkew(src, skewDeg);
    using var blurred = ApplyBlur(skewed, blurK);
    using var noisy = AddGaussianNoise(blurred, noiseSigma);
    return JpegCompress(noisy, jpegQ);
}
