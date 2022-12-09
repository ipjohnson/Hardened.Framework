using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Utilities;
using Hardened.Web.Runtime.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Hardened.Web.Runtime.StaticContent;

public record FileEntry(
    string EntryName,
    string ETagName,
    string ETag,
    string FileExt,
    string MimeType,
    bool GZippedContent,
    byte[] FileContent
);

public interface IWwwrootFileProvider
{
    ValueTask<FileEntry?> GetFileEntry(string fileName);
}

public class WwwrootFileProvider
{
    private readonly ILogger<WwwrootFileProvider> _logger;
    private readonly IStaticContentConfiguration _configuration;
    private readonly IGZipStaticContentCompressor _gZipStaticContentCompressor;
    private readonly IFileExtToMimeTypeHelper _fileExtToMimeTypeHelper;
    private readonly IETagProvider _etagProvider;
    private readonly IMemoryStreamPool _memoryStreamPool;
    private readonly ConcurrentDictionary<string, FileEntry> _cachedStaticContentEntries;
    private readonly string _rootPath;
    private readonly bool _pathExists;
}