using Shuffull.Api.Tools;
using SkiaSharp;

namespace Shuffull.Core.Tests.Tools;

public class ImageManipulatorTest
{
    [Fact]
    public void GenerateDefaultImage_ProducesRequestedDimensions()
    {
        using var image = ImageManipulator.GenerateDefaultImage(256, 128);

        Assert.Equal(256, image.Width);
        Assert.Equal(128, image.Height);
    }

    [Fact]
    public void GenerateDefaultImage_FillsWithBlack()
    {
        using var image = ImageManipulator.GenerateDefaultImage(4, 4);

        var pixel = image.GetPixel(0, 0);
        Assert.Equal(0, pixel.Red);
        Assert.Equal(0, pixel.Green);
        Assert.Equal(0, pixel.Blue);
        Assert.Equal(255, pixel.Alpha);
    }

    [Fact]
    public void ResizeWithPadding_ReturnsCanvasOfTargetSize()
    {
        using var source = Solid(800, 400, SKColors.Red);

        using var result = ImageManipulator.ResizeWithPadding(source, 512, 512);

        Assert.Equal(512, result.Width);
        Assert.Equal(512, result.Height);
    }

    [Fact]
    public void ResizeWithPadding_PadsWithBlackAroundShorterDimension()
    {
        // A wide source on a square canvas leaves black padding at the top corner.
        using var source = Solid(800, 200, SKColors.Red);

        using var result = ImageManipulator.ResizeWithPadding(source, 512, 512);

        var corner = result.GetPixel(0, 0);
        Assert.Equal(0, corner.Red);
        Assert.Equal(0, corner.Green);
        Assert.Equal(0, corner.Blue);
    }

    [Fact]
    public void ResizeWithPadding_PreservesAspectRatioOfContent()
    {
        // 800x400 (2:1) onto 512x512 -> scaled to 512x256, centered with 128px black bars top and bottom.
        using var source = Solid(800, 400, SKColors.Red);

        using var result = ImageManipulator.ResizeWithPadding(source, 512, 512);

        Assert.Equal(SKColors.Red, result.GetPixel(256, 256));
        Assert.Equal(SKColors.Red, result.GetPixel(256, 130));
        Assert.Equal(SKColors.Black, result.GetPixel(256, 125));
        Assert.Equal(SKColors.Black, result.GetPixel(256, 386));
    }

    [Fact]
    public void ResizeWithPadding_LeavesTheSourceUntouched()
    {
        using var source = Solid(800, 400, SKColors.Red);

        using var result = ImageManipulator.ResizeWithPadding(source, 512, 512);

        Assert.Equal(800, source.Width);
        Assert.Equal(400, source.Height);
    }

    [Fact]
    public void AlbumArt_RoundTripsThroughJpeg()
    {
        // SongImportService decodes embedded art, pads it to 512x512 and stores it as JPEG.
        using var source = Solid(300, 300, SKColors.Blue);
        using var png = source.Encode(SKEncodedImageFormat.Png, 100);
        using var decoded = SKBitmap.Decode(png.ToArray());

        using var result = ImageManipulator.ResizeWithPadding(decoded, 512, 512);
        using var jpeg = result.Encode(SKEncodedImageFormat.Jpeg, 75);
        using var reread = SKBitmap.Decode(jpeg.ToArray());

        Assert.Equal(512, reread.Width);
        Assert.True(reread.GetPixel(256, 256).Blue > 200);
    }

    private static SKBitmap Solid(int width, int height, SKColor color)
    {
        var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        bitmap.Erase(color);
        return bitmap;
    }
}
