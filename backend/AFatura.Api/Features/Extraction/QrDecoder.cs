using SkiaSharp;
using ZXing;
using ZXing.Common;

namespace AFatura.Api.Features.Extraction;

/// <summary>Finds QR codes in images and PDF pages (PDFium via PDFtoImage → SkiaSharp bitmap → ZXing).</summary>
public class QrDecoder(ILogger<QrDecoder> log)
{
    private const int MaxPdfPages = 6;

    public IReadOnlyList<string> Decode(byte[] data, string contentType)
    {
        var found = new List<string>();
        try
        {
            if (contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase))
            {
                using var ms = new MemoryStream(data);
                var page = 0;
                foreach (var bmp in PDFtoImage.Conversion.ToImages(ms))
                {
                    using (bmp) found.AddRange(DecodeBitmap(bmp));
                    if (found.Any(AtQrCodeParser.LooksLikeAtQr) || ++page >= MaxPdfPages) break;
                }
            }
            else
            {
                using var bmp = SKBitmap.Decode(data);
                if (bmp != null) found.AddRange(DecodeBitmap(bmp));
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "QR decoding failed");
        }
        return found.Distinct().ToList();
    }

    private static IEnumerable<string> DecodeBitmap(SKBitmap source)
    {
        var results = TryDecode(source);
        if (results.Count > 0) return results;

        // Phone photos are often huge; ZXing does better on a downscaled image. Also try upscaling tiny scans.
        foreach (var targetWidth in new[] { 1600, 1100, 2400 })
        {
            if (Math.Abs(source.Width - targetWidth) < 200) continue;
            var h = (int)(source.Height * (targetWidth / (double)source.Width));
            using var resized = source.Resize(new SKImageInfo(targetWidth, h), SKFilterQuality.Medium);
            if (resized == null) continue;
            results = TryDecode(resized);
            if (results.Count > 0) return results;
        }
        return results;
    }

    private static List<string> TryDecode(SKBitmap bmp)
    {
        using var bgra = bmp.ColorType == SKColorType.Bgra8888 ? bmp.Copy() : bmp.Copy(SKColorType.Bgra8888);
        if (bgra == null) return new List<string>();
        var pixels = bgra.Bytes;
        var source = new RGBLuminanceSource(pixels, bgra.Width, bgra.Height, RGBLuminanceSource.BitmapFormat.BGRA32);
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                TryHarder = true,
                TryInverted = true,
                PossibleFormats = [BarcodeFormat.QR_CODE]
            }
        };
        var many = reader.DecodeMultiple(source);
        if (many is { Length: > 0 }) return many.Select(r => r.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        var single = reader.Decode(source);
        return single?.Text is { Length: > 0 } t ? new List<string> { t } : new List<string>();
    }
}
