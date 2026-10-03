using System.Text;
using Emby.Plugin.SmartLists.Utilities;

namespace Emby.Plugin.SmartLists.Tests;

public class ImageContentValidatorTests
{
    public static IEnumerable<object[]> ValidHeaders()
    {
        yield return new object[] { "jpeg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F' } };
        yield return new object[] { "png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D } };
        yield return new object[] { "gif87", Encoding.ASCII.GetBytes("GIF87a\x01\x00\x01\x00") };
        yield return new object[] { "gif89", Encoding.ASCII.GetBytes("GIF89a\x01\x00\x01\x00") };
        yield return new object[] { "bmp", Encoding.ASCII.GetBytes("BM\0\0\0\0\0\0\0\0\0\0") };
        yield return new object[] { "webp", Encoding.ASCII.GetBytes("RIFF\x24\x00\x00\x00WEBPVP8 ") };
        yield return new object[] { "ico", new byte[] { 0, 0, 1, 0, 1, 0, 16, 16 } };
        yield return new object[] { "tiff-le", new byte[] { 0x49, 0x49, 0x2A, 0x00, 8, 0, 0, 0 } };
        yield return new object[] { "tiff-be", new byte[] { 0x4D, 0x4D, 0x00, 0x2A, 0, 0, 0, 8 } };
        yield return new object[] { "avif", new byte[] { 0, 0, 0, 0x1C, 0x66, 0x74, 0x79, 0x70, 0x61, 0x76, 0x69, 0x66 } };
    }

    [Theory]
    [MemberData(nameof(ValidHeaders))]
    public void AcceptsSupportedRasterSignatures(string name, byte[] header)
    {
        Assert.True(ImageContentValidator.LooksLikeRasterImage(header), name);
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>")]
    [InlineData("<?xml version=\"1.0\"?><svg/>")]
    [InlineData("<html><script>alert(1)</script></html>")]
    [InlineData("MZ\u0090\u0000 not an image")]
    [InlineData("#!/bin/sh\nrm -rf /")]
    [InlineData("PK\u0003\u0004zipfile")]
    [InlineData("")]
    [InlineData("GIF")]
    [InlineData("RIFF....WAVEfmt ")]
    public void RejectsEverythingElse(string text)
    {
        Assert.False(ImageContentValidator.LooksLikeRasterImage(Encoding.Latin1.GetBytes(text)));
    }

    [Fact]
    public void ReadHeaderReadsAtMostTheHeaderLength_AndToleratesShortStreams()
    {
        Assert.Equal(ImageContentValidator.HeaderLength, ImageContentValidator.ReadHeader(new MemoryStream(new byte[100])).Length);
        Assert.Equal(3, ImageContentValidator.ReadHeader(new MemoryStream(new byte[3])).Length);
        Assert.Empty(ImageContentValidator.ReadHeader(new MemoryStream()));
    }
}
