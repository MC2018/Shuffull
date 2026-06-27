using Shuffull.Api.Tools;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

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
        using var image = (Image<Rgba32>)ImageManipulator.GenerateDefaultImage(4, 4);

        var pixel = image[0, 0];
        Assert.Equal(0, pixel.R);
        Assert.Equal(0, pixel.G);
        Assert.Equal(0, pixel.B);
        Assert.Equal(255, pixel.A);
    }

    [Fact]
    public void ResizeWithPadding_ReturnsCanvasOfTargetSize()
    {
        using var source = new Image<Rgba32>(800, 400, Color.Red);

        using var result = ImageManipulator.ResizeWithPadding(source, 512, 512);

        Assert.Equal(512, result.Width);
        Assert.Equal(512, result.Height);
    }

    [Fact]
    public void ResizeWithPadding_PadsWithBlackAroundShorterDimension()
    {
        // A wide source on a square canvas leaves black padding at the top corner.
        using var source = new Image<Rgba32>(800, 200, Color.Red);

        using var result = (Image<Rgba32>)ImageManipulator.ResizeWithPadding(source, 512, 512);

        var corner = result[0, 0];
        Assert.Equal(0, corner.R);
        Assert.Equal(0, corner.G);
        Assert.Equal(0, corner.B);
    }

    [Fact]
    public void ResizeWithPadding_PreservesAspectRatioOfContent()
    {
        // 800x400 (2:1) onto 512x512 -> scaled to 512x256, centered with 128px black bars top and bottom.
        using var source = new Image<Rgba32>(800, 400, Color.Red);

        using var result = (Image<Rgba32>)ImageManipulator.ResizeWithPadding(source, 512, 512);

        // Center pixel should be the (red) content, not padding.
        var center = result[256, 256];
        Assert.True(center.R > center.B);
    }
}
