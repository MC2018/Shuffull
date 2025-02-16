using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace Shuffull.Site.Tools
{
    public class ImageManipulator
    {
        public static Image ResizeWithPadding(Image image, int targetWidth, int targetHeight)
        {
            // Calculate aspect-ratio-preserving resize dimensions
            double ratio = Math.Min((double)targetWidth / image.Width, (double)targetHeight / image.Height);
            int newWidth = (int)(image.Width * ratio);
            int newHeight = (int)(image.Height * ratio);

            // Resize the image while maintaining aspect ratio
            image.Mutate(ctx => ctx.Resize(newWidth, newHeight));

            // Create a new 512x512 black background image
            var finalImage = new Image<Rgba32>(targetWidth, targetHeight, Color.Black);

            // Center the resized image on the new canvas
            int x = (targetWidth - newWidth) / 2;
            int y = (targetHeight - newHeight) / 2;
            finalImage.Mutate(ctx => ctx.DrawImage(image, new Point(x, y), 1.0f));

            return finalImage;
        }

        public static Image GenerateDefaultImage(int targetWidth, int targetHeight)
        {
            return new Image<Rgba32>(targetWidth, targetHeight, Color.Black);
        }
    }
}
