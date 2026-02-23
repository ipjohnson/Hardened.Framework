using System.IO.Compression;
using Hardened.Shared.Runtime.Collections;
using Hardened.Web.Runtime.StaticContent;
using Xunit;

namespace Hardened.Web.Runtime.Tests.StaticContent;

public class GZipStaticContentCompressorTests {
    private readonly GZipStaticContentCompressor _compressor;

    public GZipStaticContentCompressorTests() {
        _compressor = new GZipStaticContentCompressor(new MemoryStreamPool());
    }

    [Fact]
    public void CompressedOutput_CanBeDecompressed_ToOriginalContent() {
        var original = "Hello, World! This is a test of GZip compression."u8.ToArray();

        var compressed = _compressor.CompressContent(original, CompressionLevel.Fastest);

        using var compressedStream = new MemoryStream(compressed);
        using var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress);
        using var resultStream = new MemoryStream();
        gzipStream.CopyTo(resultStream);

        Assert.Equal(original, resultStream.ToArray());
    }

    [Fact]
    public void EmptyInput_ProducesValidGzipOutput() {
        var compressed = _compressor.CompressContent(Array.Empty<byte>(), CompressionLevel.Fastest);

        using var compressedStream = new MemoryStream(compressed);
        using var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress);
        using var resultStream = new MemoryStream();
        gzipStream.CopyTo(resultStream);

        Assert.Empty(resultStream.ToArray());
    }

    [Theory]
    [InlineData(CompressionLevel.Fastest)]
    [InlineData(CompressionLevel.Optimal)]
    [InlineData(CompressionLevel.SmallestSize)]
    public void DifferentCompressionLevels_ProduceValidOutput(CompressionLevel level) {
        var original = "Test content for compression level validation."u8.ToArray();

        var compressed = _compressor.CompressContent(original, level);

        using var compressedStream = new MemoryStream(compressed);
        using var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress);
        using var resultStream = new MemoryStream();
        gzipStream.CopyTo(resultStream);

        Assert.Equal(original, resultStream.ToArray());
    }
}
