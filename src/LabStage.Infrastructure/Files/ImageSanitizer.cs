using SkiaSharp;

namespace LabStage.Infrastructure.Files;

/// <summary>
/// Re-draws every uploaded PNG/JPEG into a fresh image before it is stored. Only the pixels survive: camera and
/// location data (EXIF/GPS), comments, hidden payloads appended after the image and malformed chunks are all dropped.
/// Images with absurd dimensions (decompression bombs) are refused, and very large photos are scaled down.
/// </summary>
public static class ImageSanitizer
{
    /// <summary>Longest side kept; larger photos are scaled down to it (plenty for intra-oral photos and shade guides).</summary>
    public const int MaxSide = 6000;
    /// <summary>Refuse anything claiming more pixels than this before decoding it (about 160 MB of memory once decoded).</summary>
    public const long MaxPixels = 40_000_000;
    public const int JpegQuality = 90;

    public sealed record Result(byte[] Bytes, int Width, int Height);

    /// <summary>Returns the cleaned image bytes, or null with an Arabic reason when the image is refused.</summary>
    public static (Result? Image, string? Error) Sanitize(Stream input, string extension)
    {
        var format = extension.ToLowerInvariant() switch
        {
            ".png" => SKEncodedImageFormat.Png,
            ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
            _ => throw new ArgumentException("Not an image extension.", nameof(extension)),
        };

        using var data = SKData.Create(input);
        using var codec = data is null ? null : SKCodec.Create(data);
        if (codec is null) return (null, "الصورة تالفة أو بصيغة غير مدعومة.");
        var expected = format == SKEncodedImageFormat.Png ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg;
        if (codec.EncodedFormat != expected) return (null, "محتوى الصورة لا يطابق امتدادها.");

        var w = codec.Info.Width;
        var h = codec.Info.Height;
        if (w <= 0 || h <= 0 || (long)w * h > MaxPixels || w > 20_000 || h > 20_000)
            return (null, "أبعاد الصورة كبيرة جدًا. الحد الأقصى 40 ميجابكسل.");

        var info = new SKImageInfo(w, h, SKColorType.Rgba8888, codec.Info.AlphaType == SKAlphaType.Opaque ? SKAlphaType.Opaque : SKAlphaType.Premul);
        using var decoded = new SKBitmap(info);
        var res = codec.GetPixels(info, decoded.GetPixels());
        if (res is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            return (null, "الصورة تالفة أو بصيغة غير مدعومة.");

        // Apply the camera orientation now, since the EXIF tag that carried it is about to be dropped.
        using var upright = Orient(decoded, codec.EncodedOrigin);

        var scale = Math.Min(1.0, (double)MaxSide / Math.Max(upright.Width, upright.Height));
        var outW = Math.Max(1, (int)Math.Round(upright.Width * scale));
        var outH = Math.Max(1, (int)Math.Round(upright.Height * scale));

        using var surface = SKSurface.Create(new SKImageInfo(outW, outH, SKColorType.Rgba8888, upright.Info.AlphaType));
        var canvas = surface.Canvas;
        canvas.Clear(format == SKEncodedImageFormat.Jpeg ? SKColors.White : SKColors.Transparent);
        using (var src = SKImage.FromBitmap(upright))
            canvas.DrawImage(src, new SKRect(0, 0, outW, outH), new SKSamplingOptions(SKCubicResampler.Mitchell));
        using var snapshot = surface.Snapshot();
        using var encoded = snapshot.Encode(format, format == SKEncodedImageFormat.Jpeg ? JpegQuality : 100);
        if (encoded is null) return (null, "تعذّرت معالجة الصورة.");
        return (new Result(encoded.ToArray(), outW, outH), null);
    }

    private static SKBitmap Orient(SKBitmap bmp, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft) return bmp.Copy();
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var outBmp = new SKBitmap(new SKImageInfo(swap ? bmp.Height : bmp.Width, swap ? bmp.Width : bmp.Height, bmp.ColorType, bmp.AlphaType));
        using var c = new SKCanvas(outBmp);
        float W = bmp.Width, H = bmp.Height;
        switch (origin)
        {
            case SKEncodedOrigin.TopRight: c.Scale(-1, 1); c.Translate(-W, 0); break;
            case SKEncodedOrigin.BottomRight: c.RotateDegrees(180); c.Translate(-W, -H); break;
            case SKEncodedOrigin.BottomLeft: c.Scale(1, -1); c.Translate(0, -H); break;
            case SKEncodedOrigin.LeftTop: c.Scale(-1, 1); c.RotateDegrees(90); break;
            case SKEncodedOrigin.RightTop: c.Translate(H, 0); c.RotateDegrees(90); break;
            case SKEncodedOrigin.RightBottom: c.Scale(-1, 1); c.Translate(-H, W); c.RotateDegrees(-90); break;
            case SKEncodedOrigin.LeftBottom: c.Translate(0, W); c.RotateDegrees(-90); break;
        }
        c.DrawBitmap(bmp, 0, 0);
        return outBmp;
    }
}
