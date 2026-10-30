using Nut.Results;
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
    public void ToAlbumArtJpeg_PadsNonSquareArtToASquareJpeg()
    {
        using var source = Solid(600, 300, SKColors.Blue);
        using var png = source.Encode(SKEncodedImageFormat.Png, 100);

        using var art = DecodeJpeg(ImageManipulator.ToAlbumArtJpeg(png.ToArray(), 512).Get());

        Assert.Equal(512, art.Width);
        Assert.Equal(512, art.Height);
        Assert.True(art.GetPixel(256, 256).Blue > 200);
        Assert.True(art.GetPixel(256, 60).Blue < 30, "the padding above wide art should be black");
    }

    [Fact]
    public void ToAlbumArtJpeg_KeepsArtAlreadyAtTheResolution()
    {
        using var source = Solid(512, 512, SKColors.Red);
        using var png = source.Encode(SKEncodedImageFormat.Png, 100);

        using var art = DecodeJpeg(ImageManipulator.ToAlbumArtJpeg(png.ToArray(), 512).Get());

        Assert.Equal(512, art.Width);
        Assert.True(art.GetPixel(0, 0).Red > 200, "art that is already square should not be padded");
    }

    [Fact]
    public void ToAlbumArtJpeg_PutsTransparentArtOnBlack()
    {
        using var source = new SKBitmap(512, 512, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        source.Erase(SKColors.White.WithAlpha(0));
        using var png = source.Encode(SKEncodedImageFormat.Png, 100);

        using var art = DecodeJpeg(ImageManipulator.ToAlbumArtJpeg(png.ToArray(), 512).Get());

        Assert.True(art.GetPixel(256, 256).Red < 30);
    }

    [Fact]
    public void ToAlbumArtJpeg_NoArt_GivesABlackSquare()
    {
        using var art = DecodeJpeg(ImageManipulator.ToAlbumArtJpeg(null, 512).Get());

        Assert.Equal(512, art.Width);
        Assert.Equal(512, art.Height);
        Assert.True(art.GetPixel(256, 256).Red < 30);
    }

    [Fact]
    public void ToAlbumArtJpeg_UndecodableData_ReturnsError()
    {
        var result = ImageManipulator.ToAlbumArtJpeg("not an image"u8.ToArray(), 512);

        Assert.True(result.IsError);
    }

    private static SKBitmap DecodeJpeg(byte[] bytes)
    {
        using var codec = SKCodec.Create(new MemoryStream(bytes));
        Assert.Equal(SKEncodedImageFormat.Jpeg, codec.EncodedFormat);
        return SKBitmap.Decode(codec);
    }

    private static SKBitmap Solid(int width, int height, SKColor color)
    {
        var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        bitmap.Erase(color);
        return bitmap;
    }
}
