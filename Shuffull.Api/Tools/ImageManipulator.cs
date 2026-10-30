using Nut.Results;
using SkiaSharp;

namespace Shuffull.Api.Tools
{
    public class ImageManipulator
    {
        // Trilinear: mipmaps keep a large downscale (e.g. 3000x3000 cover art -> 512) from aliasing.
        private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

        // What ImageSharp's JpegEncoder defaulted to before the swap.
        private const int AlbumArtJpegQuality = 75;

        /// <summary>
        /// Pads embedded album art onto a black <paramref name="resolution"/> square and encodes it as JPEG.
        /// Null bytes (no art, or a format we don't take) give a plain black square.
        /// </summary>
        public static Result<byte[]> ToAlbumArtJpeg(byte[]? imageBytes, int resolution)
        {
            SKBitmap art;
            if (imageBytes == null)
            {
                art = GenerateDefaultImage(resolution, resolution);
            }
            else
            {
                // SKBitmap.Decode(byte[]) throws when no codec recognises the bytes, so open the codec first.
                using var codec = SKCodec.Create(new MemoryStream(imageBytes));
                using var decoded = codec == null ? null : SKBitmap.Decode(codec);
                if (decoded == null)
                {
                    return Result.Error<byte[]>("Album art could not be decoded.");
                }
                // Always redrawn, even at the right size, so transparent art lands on black rather than on
                // whatever the JPEG encoder makes of the alpha channel.
                art = ResizeWithPadding(decoded, resolution, resolution);
            }

            using (art)
            using (var jpeg = art.Encode(SKEncodedImageFormat.Jpeg, AlbumArtJpegQuality))
            {
                // Unlike ImageSharp's Save, Encode reports failure by returning null rather than throwing.
                return jpeg == null
                    ? Result.Error<byte[]>("Album art could not be encoded as JPEG.")
                    : Result.Ok(jpeg.ToArray());
            }
        }

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
