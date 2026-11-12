using AFatura.Api.Common;
using SkiaSharp;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;

namespace AFatura.Api.Features.Invoices;

/// <summary>
/// Junta várias fotos/PDFs (páginas de uma fatura longa) num único PDF, pela ordem recebida.
/// Fotos: orientação EXIF corrigida, reduzidas a ~2600 px e guardadas como JPEG numa página com a mesma proporção.
/// PDFs: todas as páginas copiadas tal como estão.
/// </summary>
public static class DocumentMerger
{
    public record Part(string FileName, string? ContentType, byte[] Bytes);

    private const double PageWidth = 595;      // A4 em pontos
    private const double MaxPageHeight = 14000; // limite prático do formato PDF (14 400 pt)
    private const int MaxImageSide = 2600;

    public static bool IsPdf(Part p) =>
        p.ContentType == "application/pdf" || p.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
        (p.Bytes.Length > 4 && p.Bytes[0] == '%' && p.Bytes[1] == 'P' && p.Bytes[2] == 'D' && p.Bytes[3] == 'F');

    public static byte[] Merge(IReadOnlyList<Part> parts)
    {
        if (parts.Count == 0) throw new AppException(400, "Nenhum ficheiro enviado");
        var builder = new PdfDocumentBuilder();
        foreach (var part in parts)
        {
            if (IsPdf(part))
            {
                using var doc = PdfDocument.Open(part.Bytes);
                for (var i = 1; i <= doc.NumberOfPages; i++) builder.AddPage(doc, i);
                continue;
            }

            var (jpeg, w, h) = NormalizeImage(part);
            var pageHeight = PageWidth * h / w;
            var pageWidth = PageWidth;
            if (pageHeight > MaxPageHeight) { pageWidth = PageWidth * MaxPageHeight / pageHeight; pageHeight = MaxPageHeight; }
            var page = builder.AddPage(pageWidth, pageHeight);
            page.AddJpeg(jpeg, new PdfRectangle(0, 0, pageWidth, pageHeight));
        }
        return builder.Build();
    }

    private static (byte[] Jpeg, int Width, int Height) NormalizeImage(Part part)
    {
        using var data = SKData.CreateCopy(part.Bytes);
        using var codec = SKCodec.Create(data) ?? throw new AppException(400, $"Formato de imagem não suportado: {part.FileName}");
        using var decoded = SKBitmap.Decode(codec) ?? throw new AppException(400, $"Não foi possível ler a imagem {part.FileName}");

        using var oriented = ApplyOrientation(decoded, codec.EncodedOrigin);
        var src = oriented ?? decoded;

        var scale = Math.Min(1.0, (double)MaxImageSide / Math.Max(src.Width, src.Height));
        SKBitmap? resized = null;
        if (scale < 1.0)
        {
            var info = new SKImageInfo(Math.Max(1, (int)(src.Width * scale)), Math.Max(1, (int)(src.Height * scale)), SKColorType.Rgba8888, SKAlphaType.Premul);
            resized = src.Resize(info, SKFilterQuality.High);
        }
        try
        {
            var final = resized ?? src;
            using var image = SKImage.FromBitmap(final);
            using var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, 85);
            return (jpeg.ToArray(), final.Width, final.Height);
        }
        finally { resized?.Dispose(); }
    }

    /// <summary>Roda/espelha conforme a orientação EXIF; devolve null se não for preciso.</summary>
    private static SKBitmap? ApplyOrientation(SKBitmap bmp, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft) return null;
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var result = new SKBitmap(swap ? bmp.Height : bmp.Width, swap ? bmp.Width : bmp.Height);
        using var canvas = new SKCanvas(result);
        switch (origin)
        {
            case SKEncodedOrigin.TopRight: canvas.Scale(-1, 1, bmp.Width / 2f, 0); break;
            case SKEncodedOrigin.BottomRight: canvas.RotateDegrees(180, bmp.Width / 2f, bmp.Height / 2f); break;
            case SKEncodedOrigin.BottomLeft: canvas.Scale(1, -1, 0, bmp.Height / 2f); break;
            case SKEncodedOrigin.LeftTop: canvas.RotateDegrees(90); canvas.Scale(1, -1); break;
            case SKEncodedOrigin.RightTop: canvas.Translate(result.Width, 0); canvas.RotateDegrees(90); break;
            case SKEncodedOrigin.RightBottom: canvas.Translate(result.Width, result.Height); canvas.RotateDegrees(90); canvas.Scale(-1, 1); break;
            case SKEncodedOrigin.LeftBottom: canvas.Translate(0, result.Height); canvas.RotateDegrees(270); break;
        }
        canvas.DrawBitmap(bmp, 0, 0);
        return result;
    }
}
