using SkiaSharp;
using ZXing;
using ZXing.Common;

namespace AFatura.Api.Features.Extraction;

/// <summary>Finds QR codes in images and PDF pages (PDFium via PDFtoImage → SkiaSharp bitmap → ZXing).</summary>
public class QrDecoder(ILogger<QrDecoder> log)
{
    private const int MaxPdfPages = 40; // statements such as Via Verde carry one QR code per invoice, on many pages

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
                    if (++page >= MaxPdfPages) break;
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

        // Dark / low-contrast photos (thermal receipts, poor light): grayscale + contrast stretch, then retry.
        foreach (var contrast in new[] { 1.6f, 2.4f })
        {
            results = TryDecodeEnhanced(source, contrast);
            if (results.Count > 0) return results;
        }
        return results;
    }

    /// <summary>Luminance with a contrast stretch around the image's mean brightness, decoded as an 8-bit gray image.</summary>
    private static List<string> TryDecodeEnhanced(SKBitmap source, float contrast)
    {
        using var bgra = source.ColorType == SKColorType.Bgra8888 ? source.Copy() : source.Copy(SKColorType.Bgra8888);
        if (bgra == null) return new List<string>();
        var px = bgra.Bytes;
        int w = bgra.Width, h = bgra.Height, stride = bgra.RowBytes;
        var gray = new byte[w * h];
        long sum = 0;
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var o = y * stride + x * 4;
            var l = (px[o + 2] * 299 + px[o + 1] * 587 + px[o] * 114) / 1000;
            gray[y * w + x] = (byte)l;
            sum += l;
        }
        var mean = sum / (float)(w * h);
        for (var i = 0; i < gray.Length; i++)
            gray[i] = (byte)Math.Clamp((gray[i] - mean) * contrast + 128f, 0f, 255f);

        var reader = NewReader();
        var lum = new RGBLuminanceSource(gray, w, h, RGBLuminanceSource.BitmapFormat.Gray8);
        var many = reader.DecodeMultiple(lum);
        if (many is { Length: > 0 }) return many.Select(r => r.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        var single = reader.Decode(lum);
        return single?.Text is { Length: > 0 } t ? new List<string> { t } : new List<string>();
    }

    private static BarcodeReaderGeneric NewReader() => new()
    {
        AutoRotate = true,
        Options = new DecodingOptions { TryHarder = true, TryInverted = true, PossibleFormats = [BarcodeFormat.QR_CODE] }
    };

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
