using SkiaSharp;

namespace Shuffull.Api.Tools
{
    public class ImageManipulator
    {
        // Trilinear: mipmaps keep a large downscale (e.g. 3000x3000 cover art -> 512) from aliasing.
        private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

        public static SKBitmap ResizeWithPadding(SKBitmap image, int targetWidth, int targetHeight)
        {
            // Calculate aspect-ratio-preserving resize dimensions
            double ratio = Math.Min((double)targetWidth / image.Width, (double)targetHeight / image.Height);
            int newWidth = (int)(image.Width * ratio);
            int newHeight = (int)(image.Height * ratio);

            // Center the resized image on a black canvas of the target size
            int x = (targetWidth - newWidth) / 2;
            int y = (targetHeight - newHeight) / 2;
            var finalImage = GenerateDefaultImage(targetWidth, targetHeight);
            using var source = SKImage.FromBitmap(image);
            using var canvas = new SKCanvas(finalImage);
            canvas.DrawImage(source, SKRect.Create(x, y, newWidth, newHeight), Sampling);

            return finalImage;
        }

        public static SKBitmap GenerateDefaultImage(int targetWidth, int targetHeight)
        {
            var image = new SKBitmap(targetWidth, targetHeight, SKColorType.Rgba8888, SKAlphaType.Opaque);
            image.Erase(SKColors.Black);
            return image;
        }
    }
}
