using System.IO.Compression;
using Hardened.Shared.Runtime.Collections;

namespace Hardened.Web.Runtime.StaticContent;

public interface IGZipStaticContentCompressor
{
    byte[] CompressContent(byte[] bytes, CompressionLevel compressionLevel = CompressionLevel.Fastest);
}

public class GZipStaticContentCompressor : IGZipStaticContentCompressor
{
    private readonly IMemoryStreamPool _memoryStreamPool;

    public GZipStaticContentCompressor(IMemoryStreamPool memoryStreamPool)
    {
        _memoryStreamPool = memoryStreamPool;
    }

    public byte[] CompressContent(byte[] bytes, CompressionLevel compressionLevel)
    {
        using var memoryStreamRes = _memoryStreamPool.Get();
        using var gzipStream = new GZipStream(memoryStreamRes.Item, compressionLevel, true);

        gzipStream.Write(bytes, 0, bytes.Length);
        gzipStream.Flush();

        return memoryStreamRes.Item.ToArray();
    }
}