using System.Buffers.Binary;
using LabStage.Infrastructure.Files;
using SkiaSharp;

namespace LabStage.Tests.Api;

public class ImageSanitizerTests
{
    private static byte[] Png(int w, int h)
    {
        using var bmp = new SKBitmap(w, h);
        bmp.Erase(SKColors.CornflowerBlue);
        using var img = SKImage.FromBitmap(bmp);
        return img.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }

    [Fact]
    public void Keeps_the_pixels_and_drops_anything_hidden_after_the_image()
    {
        var evil = Png(30, 20).Concat("<?php system($_GET[1]); ?>"u8.ToArray()).ToArray();
        var (img, err) = ImageSanitizer.Sanitize(new MemoryStream(evil), ".png");
        Assert.Null(err);
        Assert.Equal((30, 20), (img!.Width, img.Height));
        Assert.DoesNotContain("php", System.Text.Encoding.Latin1.GetString(img.Bytes));
    }

    [Fact]
    public void Scales_down_very_large_photos()
    {
        var (img, _) = ImageSanitizer.Sanitize(new MemoryStream(Png(ImageSanitizer.MaxSide + 1000, 100)), ".png");
        Assert.Equal(ImageSanitizer.MaxSide, img!.Width);
    }

    [Fact]
    public void Refuses_decompression_bombs_and_wrong_content()
    {
        // A PNG whose header claims 30000 x 30000 pixels (900 megapixels).
        var png = Png(1, 1);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16), 30_000);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20), 30_000);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(29), Crc32(png.AsSpan(12, 17)));
        var (bomb, bombErr) = ImageSanitizer.Sanitize(new MemoryStream(png), ".png");
        Assert.Null(bomb);
        Assert.NotNull(bombErr);

        var (wrong, wrongErr) = ImageSanitizer.Sanitize(new MemoryStream(Png(4, 4)), ".jpg");
        Assert.Null(wrong);
        Assert.NotNull(wrongErr);

        var (broken, _) = ImageSanitizer.Sanitize(new MemoryStream("not an image at all"u8.ToArray()), ".png");
        Assert.Null(broken);
    }
}
