using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Docnet.Core;
using Docnet.Core.Models;
using SkiaSharp;

if (args.Length == 0)
{
    Console.WriteLine("Usage: GemmaOcrTest <file-path> [prompt]");
    Console.WriteLine("  file-path: Path to an image (PNG, JPG, WebP) or PDF file");
    Console.WriteLine("  prompt:    Optional custom prompt (default: extract all text)");
    return 1;
}

var filePath = Path.GetFullPath(args[0]);
if (!File.Exists(filePath))
{
    Console.Error.WriteLine($"File not found: {filePath}");
    return 1;
}

var prompt = args.Length > 1
    ? string.Join(' ', args.Skip(1))
    : "Extract all text from this image. Return only the extracted text, preserving the original formatting as closely as possible.";

using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
var ollamaUrl = Environment.GetEnvironmentVariable("OLLAMA_HOST") ?? "http://localhost:11434";

var isPdf = Path.GetExtension(filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

if (isPdf)
{
    var pageImages = ConvertPdfToPngImages(filePath);
    Console.Error.WriteLine($"PDF has {pageImages.Count} page(s)");

    for (int i = 0; i < pageImages.Count; i++)
    {
        Console.Error.WriteLine($"\n--- Processing page {i + 1}/{pageImages.Count} ---");
        var result = await SendToGemma(httpClient, ollamaUrl, pageImages[i], prompt,
            $"page {i + 1}", pageImages[i].Length / 1024);

        if (pageImages.Count > 1)
            Console.WriteLine($"\n=== PAGE {i + 1} ===");
        Console.WriteLine(result);
    }
}
else
{
    var imageBytes = await File.ReadAllBytesAsync(filePath);
    Console.Error.WriteLine($"Sending image to Gemma 4 ({Path.GetFileName(filePath)}, {imageBytes.Length / 1024} KB)...");
    var result = await SendToGemma(httpClient, ollamaUrl, imageBytes, prompt,
        Path.GetFileName(filePath), imageBytes.Length / 1024);
    Console.WriteLine(result);
}

return 0;

static async Task<string> SendToGemma(HttpClient httpClient, string ollamaUrl, byte[] imageBytes, string prompt, string label, long sizeKb)
{
    var base64Image = Convert.ToBase64String(imageBytes);
    var request = new OllamaGenerateRequest
    {
        Model = "gemma4:e4b",
        Prompt = prompt,
        Images = [base64Image],
        Stream = false
    };

    Console.Error.WriteLine($"Sending {label} ({sizeKb} KB) to Gemma 4...");

    try
    {
        var response = await httpClient.PostAsJsonAsync($"{ollamaUrl}/api/generate", request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>();
        return result?.Response ?? "(no response)";
    }
    catch (HttpRequestException ex)
    {
        Console.Error.WriteLine($"Error connecting to Ollama at {ollamaUrl}: {ex.Message}");
        Console.Error.WriteLine("Make sure Ollama is running (ollama serve)");
        Environment.Exit(1);
        return "";
    }
    catch (TaskCanceledException)
    {
        Console.Error.WriteLine("Request timed out. The image may be too large or the model is still loading.");
        Environment.Exit(1);
        return "";
    }
}

/// <summary>
/// Convert PDF pages to PNG images using Docnet.Core + SkiaSharp (pure .NET, no Python dependency).
/// Renders each page at approximately 200 DPI.
/// </summary>
static List<byte[]> ConvertPdfToPngImages(string pdfPath)
{
    // 200 DPI: Letter page is 8.5x11 => 1700x2200 pixels
    const int pageWidth = 1700;
    const int pageHeight = 2200;

    using var docReader = DocLib.Instance.GetDocReader(pdfPath, new PageDimensions(pageWidth, pageHeight));
    var pageCount = docReader.GetPageCount();
    var images = new List<byte[]>(pageCount);

    for (int i = 0; i < pageCount; i++)
    {
        using var pageReader = docReader.GetPageReader(i);
        var rawBytes = pageReader.GetImage();
        var width = pageReader.GetPageWidth();
        var height = pageReader.GetPageHeight();

        Console.Error.WriteLine($"  Page {i + 1}: {width}x{height}, raw bytes: {rawBytes.Length}");

        // Docnet returns BGRA8888
        using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        Marshal.Copy(rawBytes, 0, bitmap.GetPixels(), rawBytes.Length);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 85);
        images.Add(data.ToArray());
    }

    return images;
}

record OllamaGenerateRequest
{
    [JsonPropertyName("model")] public string Model { get; init; } = "";
    [JsonPropertyName("prompt")] public string Prompt { get; init; } = "";
    [JsonPropertyName("images")] public string[] Images { get; init; } = [];
    [JsonPropertyName("stream")] public bool Stream { get; init; }
}

record OllamaGenerateResponse
{
    [JsonPropertyName("response")] public string Response { get; init; } = "";
}
