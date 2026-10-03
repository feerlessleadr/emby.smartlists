using System;
using System.IO;

namespace Emby.Plugin.SmartLists.Utilities
{
    /// <summary>
    /// Checks that uploaded or restored image bytes really are one of the raster formats the plugin accepts, by
    /// signature rather than by the file name or the client-supplied content type. SVG is deliberately not accepted:
    /// it is an active format (scripts) and the images are served from the Emby server's origin.
    /// </summary>
    public static class ImageContentValidator
    {
        /// <summary>
        /// Number of leading bytes <see cref="LooksLikeRasterImage"/> needs.
        /// </summary>
        public const int HeaderLength = 16;

        /// <summary>
        /// Gets whether <paramref name="header"/> (the first bytes of a file) starts with a supported raster signature.
        /// </summary>
        /// <param name="header">The leading bytes of the file.</param>
        /// <returns>True when the signature is JPEG, PNG/APNG, GIF, BMP, WebP, ICO, TIFF or AVIF.</returns>
        public static bool LooksLikeRasterImage(ReadOnlySpan<byte> header)
        {
            if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            {
                return true; // JPEG
            }

            if (header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            {
                return true; // PNG / APNG
            }

            if (header.Length >= 6 && (header[..6].SequenceEqual("GIF87a"u8) || header[..6].SequenceEqual("GIF89a"u8)))
            {
                return true;
            }

            if (header.Length >= 2 && header[0] == (byte)'B' && header[1] == (byte)'M')
            {
                return true; // BMP
            }

            if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
            {
                return true;
            }

            if (header.Length >= 4 && header[..4].SequenceEqual(new byte[] { 0x00, 0x00, 0x01, 0x00 }))
            {
                return true; // ICO
            }

            if (header.Length >= 4 && (header[..4].SequenceEqual(new byte[] { 0x49, 0x49, 0x2A, 0x00 }) || header[..4].SequenceEqual(new byte[] { 0x4D, 0x4D, 0x00, 0x2A })))
            {
                return true; // TIFF
            }

            if (header.Length >= 12 && header[4..8].SequenceEqual("ftyp"u8)
                && (header[8..12].SequenceEqual("avif"u8) || header[8..12].SequenceEqual("avis"u8)))
            {
                return true; // AVIF
            }

            return false;
        }

        /// <summary>
        /// Reads up to <see cref="HeaderLength"/> bytes from <paramref name="stream"/> into a new array.
        /// </summary>
        /// <param name="stream">The stream, positioned at the start of the file.</param>
        /// <returns>The bytes that were read (possibly fewer than <see cref="HeaderLength"/>).</returns>
        public static byte[] ReadHeader(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            var buffer = new byte[HeaderLength];
            var total = 0;
            while (total < buffer.Length)
            {
                var read = stream.Read(buffer, total, buffer.Length - total);
                if (read == 0)
                {
                    break;
                }

                total += read;
            }

            return total == buffer.Length ? buffer : buffer[..total];
        }
    }
}
