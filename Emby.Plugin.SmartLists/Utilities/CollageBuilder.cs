using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Emby.Plugin.SmartLists.Utilities
{
    /// <summary>
    /// Builds the 2x2 grid collage JPEGs used as auto-generated covers for smart
    /// playlists and collections. Shared by PlaylistService and CollectionService.
    /// </summary>
    public static class CollageBuilder
    {
        /// <summary>
        /// Filename for the plugin's auto-generated Primary cover (collage or cropped copy).
        /// CollectionService's manual-image classification whitelists this exact name.
        /// </summary>
        public const string CollageFileName = "smartlist-collage.jpg";

        /// <summary>
        /// Filename for the plugin's auto-generated Thumb cover.
        /// </summary>
        public const string ThumbCollageFileName = "smartlist-thumb-collage.jpg";

        /// <summary>
        /// JPEG quality for all generated covers.
        /// </summary>
        private const int JpegQuality = 90;

        /// <summary>
        /// Decoder options for every image the plugin reads from disk. Only the formats covers actually
        /// use are registered: TIFF is left out on purpose, because the TIFF decoder of the bundled
        /// ImageSharp (3.1.x) has an unfixed denial-of-service bug (GHSA-wmxv-xphr-5c9g) and nothing here
        /// needs it. A TIFF is rejected as an unknown format instead of being decoded.
        /// </summary>
        private static readonly DecoderOptions SafeDecoderOptions = new()
        {
            Configuration = new SixLabors.ImageSharp.Configuration(
                new JpegConfigurationModule(),
                new PngConfigurationModule(),
                new GifConfigurationModule(),
                new WebpConfigurationModule(),
                new BmpConfigurationModule())
            {
                // Covers are decoded in the Emby server process from files anyone with access to the media
                // folders (or a metadata provider) can supply, so decoding must not be able to take the server down.
                MemoryAllocator = SixLabors.ImageSharp.Memory.MemoryAllocator.Create(new SixLabors.ImageSharp.Memory.MemoryAllocatorOptions { AllocationLimitMegabytes = 512 }),
            },
            MaxFrames = 1, // a cover is one picture: an animated GIF or WebP is read as its first frame only
        };

        /// <summary>The largest source picture decoded, in pixels (a 4K poster is about 8 million).</summary>
        private const long MaxSourcePixels = 40_000_000;

        /// <summary>The largest source file decoded, in bytes.</summary>
        private const long MaxSourceBytes = 50L * 1024 * 1024;

        /// <summary>
        /// Loads a cover source safely: refuses a file that is too big, reads only the header to refuse a picture that
        /// declares too many pixels (a tiny file can claim billions), and then decodes with <see cref="SafeDecoderOptions"/>.
        /// A refusal is an exception the callers already handle by skipping that source.
        /// </summary>
        /// <param name="path">Path of the picture file.</param>
        /// <returns>The decoded image.</returns>
        private static Image LoadSafely(string path)
        {
            if (new FileInfo(path).Length > MaxSourceBytes)
            {
                throw new InvalidDataException("The picture file is too large to use as a cover.");
            }

            var info = Image.Identify(SafeDecoderOptions, path);
            if (info == null || (long)info.Width * info.Height > MaxSourcePixels)
            {
                throw new InvalidDataException("The picture has too many pixels to use as a cover.");
            }

            return Image.Load(SafeDecoderOptions, path);
        }

        /// <summary>
        /// Gets the Emby tile aspect ratio covers are cropped to for an image type:
        /// playlists use square Primary tiles, collections 2:3 poster tiles; Thumb tiles
        /// are 16:9 for both. Single policy point - the upload paths and the generated-cover
        /// paths must stay in lockstep.
        /// </summary>
        private static (int Width, int Height) GetTileAspect(MediaBrowser.Model.Entities.ImageType imageType, bool forPlaylist)
            => imageType == MediaBrowser.Model.Entities.ImageType.Thumb ? (16, 9) : forPlaylist ? (1, 1) : (2, 3);

        /// <summary>
        /// Creates a badged, center-cropped copy of a cover at the Emby tile aspect ratio
        /// for the given image type. Shared by the upload and generated-cover paths of both
        /// playlists and collections so the crop policy stays in one place.
        /// </summary>
        /// <param name="sourcePath">The source image path.</param>
        /// <param name="outputPath">The destination path.</param>
        /// <param name="imageType">The Emby image type (drives the tile aspect ratio).</param>
        /// <param name="forPlaylist">True for playlist covers (square Primary tiles), false for collections (2:3).</param>
        /// <param name="targetWidth">Output width in pixels, or 0 to crop at native resolution.</param>
        /// <param name="logger">Logger for failures.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>False when the source cannot be decoded or the copy cannot be saved -
        /// the caller should fall back to the unmodified source.</returns>
        public static Task<bool> TryCreateBadgedTileCoverAsync(
            string sourcePath,
            string outputPath,
            MediaBrowser.Model.Entities.ImageType imageType,
            bool forPlaylist,
            int targetWidth,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var (aspectWidth, aspectHeight) = GetTileAspect(imageType, forPlaylist);
            return TryCreateCroppedCoverAsync(sourcePath, outputPath, aspectWidth, aspectHeight, targetWidth, applyBadge: true, logger, cancellationToken);
        }

        /// <summary>
        /// Creates a 2x2 collage from up to four source images and saves it as a JPEG.
        /// Images are center-cropped to fill their quadrant. Unreadable sources are
        /// skipped (their quadrant stays black), matching previous behavior.
        /// </summary>
        /// <param name="imagePaths">Source image paths (first four are used).</param>
        /// <param name="outputPath">Destination .jpg path.</param>
        /// <param name="width">Collage width in pixels (quadrants are width/2).</param>
        /// <param name="height">Collage height in pixels (quadrants are height/2).</param>
        /// <param name="applyBadge">Whether to stamp the smart list badge on the result.</param>
        /// <param name="logger">Logger for per-image load failures.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public static Task CreateGridCollageAsync(
            IReadOnlyList<string> imagePaths,
            string outputPath,
            int width,
            int height,
            bool applyBadge,
            ILogger logger,
            CancellationToken cancellationToken)
            => Task.Run(() => CreateGridCollage(imagePaths, outputPath, width, height, applyBadge, logger, cancellationToken), cancellationToken);

        // The ImageSharp work is synchronous on purpose. An async method gets a compiler-generated state-machine
        // struct whose awaiter fields (e.g. TaskAwaiter<Image>) force the CLR to load ImageSharp types as soon as
        // anyone enumerates this class's nested types. Emby does that for every plugin assembly at start-up, before
        // the plugin can register its assembly resolver, and logs a loader error. Keeping ImageSharp types out of
        // async state machines avoids it.
        private static void CreateGridCollage(
            IReadOnlyList<string> imagePaths,
            string outputPath,
            int width,
            int height,
            bool applyBadge,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var quadrantWidth = width / 2;
            var quadrantHeight = height / 2;

            using var collage = new Image<Rgba32>(width, height);
            collage.Mutate(x => x.BackgroundColor(Color.Black));

            var positions = new[]
            {
                new Point(0, 0),
                new Point(quadrantWidth, 0),
                new Point(0, quadrantHeight),
                new Point(quadrantWidth, quadrantHeight)
            };

            for (int i = 0; i < 4 && i < imagePaths.Count; i++)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var sourceImage = LoadSafely(imagePaths[i]);

                    // Rotate EXIF-oriented sources (e.g. phone photos) into the display frame
                    // before any geometry runs on their pixel grid.
                    sourceImage.Mutate(x => x.AutoOrient());

                    // Resize image to fit quadrant while maintaining aspect ratio
                    var resizeOptions = new ResizeOptions
                    {
                        Size = new Size(quadrantWidth, quadrantHeight),
                        Mode = ResizeMode.Crop,
                        Position = AnchorPositionMode.Center
                    };

                    sourceImage.Mutate(x => x.Resize(resizeOptions));
                    collage.Mutate(ctx => ctx.DrawImage(sourceImage, positions[i], 1.0f));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Failed to load image {ImagePath} for collage, skipping", imagePaths[i]);
                }
            }

            if (applyBadge)
            {
                CoverBadgeHelper.ApplyBadge(collage, logger);
            }

            SaveCover(collage, outputPath);
        }

        /// <summary>
        /// Creates a center-cropped copy of a single source image at the given aspect ratio,
        /// optionally stamping the smart list badge. Used for covers when the badge is enabled:
        /// Emby tiles crop covers (square for playlists, poster/thumb ratios for
        /// collections), so a badge in the corner of an off-ratio cover would fall outside the
        /// visible area — and off-ratio covers also distort overview grids. The encoder is
        /// inferred from the output extension (JPEG saved at quality 90).
        /// </summary>
        /// <param name="sourcePath">The source image path.</param>
        /// <param name="outputPath">The destination path.</param>
        /// <param name="aspectWidth">Width component of the target aspect ratio (e.g. 1, 2, 16).</param>
        /// <param name="aspectHeight">Height component of the target aspect ratio (e.g. 1, 3, 9).</param>
        /// <param name="targetWidth">Output width in pixels, or 0 to crop at the source's native
        /// resolution (largest crop of the target ratio that fits, no scaling).</param>
        /// <param name="applyBadge">Whether to stamp the smart list badge on the result.</param>
        /// <param name="logger">Logger for failures.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>False when the source cannot be decoded or the copy cannot be saved.</returns>
        public static Task<bool> TryCreateCroppedCoverAsync(
            string sourcePath,
            string outputPath,
            int aspectWidth,
            int aspectHeight,
            int targetWidth,
            bool applyBadge,
            ILogger logger,
            CancellationToken cancellationToken)
            => Task.Run(() => TryCreateCroppedCover(sourcePath, outputPath, aspectWidth, aspectHeight, targetWidth, applyBadge, logger, cancellationToken), cancellationToken);

        private static bool TryCreateCroppedCover(
            string sourcePath,
            string outputPath,
            int aspectWidth,
            int aspectHeight,
            int targetWidth,
            bool applyBadge,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var image = LoadSafely(sourcePath);

                // Rotate EXIF-oriented sources into the display frame BEFORE the crop
                // geometry below reads Width/Height, and so the saved copy carries no
                // orientation tag for clients to re-apply.
                image.Mutate(x => x.AutoOrient());

                int cropWidth;
                int cropHeight;
                if (targetWidth > 0)
                {
                    cropWidth = targetWidth;
                    cropHeight = targetWidth * aspectHeight / aspectWidth;
                }
                else if (image.Width * aspectHeight >= image.Height * aspectWidth)
                {
                    // Source is wider than the target ratio: full height, cropped width
                    cropHeight = image.Height;
                    cropWidth = image.Height * aspectWidth / aspectHeight;
                }
                else
                {
                    // Source is taller than the target ratio: full width, cropped height
                    cropWidth = image.Width;
                    cropHeight = image.Width * aspectHeight / aspectWidth;
                }

                // Degenerate sources (1px edges) can truncate a dimension to 0, which
                // ImageSharp would interpret as "preserve aspect", silently skipping the crop.
                cropWidth = Math.Max(1, cropWidth);
                cropHeight = Math.Max(1, cropHeight);

                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(cropWidth, cropHeight),
                    Mode = ResizeMode.Crop,
                    Position = AnchorPositionMode.Center
                }));

                if (applyBadge)
                {
                    CoverBadgeHelper.ApplyBadge(image, logger);
                }

                SaveCover(image, outputPath);
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Could not create cropped cover from {SourcePath}", sourcePath);
                return false;
            }
        }

        /// <summary>
        /// Saves a cover with the encoder inferred from the output extension; JPEG output
        /// uses the plugin's standard quality.
        /// </summary>
        private static void SaveCover(Image image, string outputPath)
        {
            var extension = System.IO.Path.GetExtension(outputPath);
            if (string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                image.Save(outputPath, new JpegEncoder { Quality = JpegQuality });
                return;
            }

            image.Save(outputPath);
        }
    }
}
