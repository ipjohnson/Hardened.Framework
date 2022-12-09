using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using Hardened.Requests.Abstract.Execution;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Utilities;
using Hardened.Web.Runtime.Configuration;
using Hardened.Web.Runtime.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Hardened.Web.Runtime.StaticContent;

public interface IStaticContentHandler
{
    Task<bool> Handle(IExecutionContext context);
}

public class StaticContentHandler : IStaticContentHandler
{
    private static readonly Task<bool> FalseComplete = Task.FromResult(false);
    private static readonly Task<bool> TrueComplete = Task.FromResult(true);
    private const string GzFileExtension = ".gz";
    private const string BrFileExtension = ".br";

    private readonly ILogger<StaticContentHandler> _logger;
    private readonly IStaticContentConfiguration _configuration;
    private readonly IGZipStaticContentCompressor _gZipStaticContentCompressor;
    private readonly IFileExtToMimeTypeHelper _fileExtToMimeTypeHelper;
    private readonly IETagProvider _etagProvider;
    private readonly IMemoryStreamPool _memoryStreamPool;
    private readonly ConcurrentDictionary<string, CachedStaticContentEntry> _cachedStaticContentEntries;
    private readonly string _rootPath;
    private readonly bool _pathExists;

    public StaticContentHandler(
        IOptions<IStaticContentConfiguration> configuration, 
        IFileExtToMimeTypeHelper fileExtToMimeTypeHelper,
        IGZipStaticContentCompressor gZipStaticContentCompressor, 
        IETagProvider etagProvider, 
        IMemoryStreamPool memoryStreamPool,
        ILogger<StaticContentHandler> logger)
    {
        _fileExtToMimeTypeHelper = fileExtToMimeTypeHelper;
        _gZipStaticContentCompressor = gZipStaticContentCompressor;
        _etagProvider = etagProvider;
        _logger = logger;
        _memoryStreamPool = memoryStreamPool;
        _configuration = configuration.Value;
        _cachedStaticContentEntries = new ConcurrentDictionary<string, CachedStaticContentEntry>();
        
        _rootPath = Path.Combine( Directory.GetCurrentDirectory(), _configuration.Path);
        _pathExists = Directory.Exists(_rootPath);

        if (!_pathExists)
        {
            _rootPath = 
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().GetName().CodeBase) ?? "";

            if (!string.IsNullOrEmpty(_rootPath))
            {
                _rootPath = _rootPath.Substring(6);

                _rootPath = Path.Combine(_rootPath, _configuration.Path);
                _pathExists = Directory.Exists(_rootPath);
            }
        }
    }
        
    public Task<bool> Handle(IExecutionContext context)
    {
        if (!_pathExists)
        {
            return FalseComplete;
        }

        return HandleRequestForPath(context, context.Request.Path);
    }

    private Task<bool> HandleRequestForPath(IExecutionContext context, string requestPath)
    {
        if (_cachedStaticContentEntries.TryGetValue(requestPath, out var cacheEntry))
        {
            return RespondWithContent(context, cacheEntry);
        }

        var filePath = Path.Combine(_rootPath, requestPath.TrimStart('/'));

        if (File.Exists(filePath))
        {
            return ReturnFile(context, filePath);
        }

        if (File.Exists(filePath + GzFileExtension))
        {
            return ReturnCompressedFile(context, filePath, GzFileExtension, "gzip");
        }

        if (File.Exists(filePath + BrFileExtension))
        {
            return ReturnCompressedFile(context, filePath, BrFileExtension, "br");
        }

        if (!string.IsNullOrEmpty(_configuration.FallBackFile))
        {
            return ReturnDefaultFile(context, requestPath);
        }

        return FalseComplete;
    }

    private async Task<bool> ReturnFile(IExecutionContext context, string filePath)
    {
        var (contentType, isBinary) = _fileExtToMimeTypeHelper.GetMimeTypeInfo(Path.GetExtension(filePath));

        var fileBytes = await File.ReadAllBytesAsync(filePath);
        byte[]? compressedBytes = null;
        string contentEncoding = "";

        var etag = _etagProvider.GenerateETag(fileBytes);

        if (_configuration.CompressTextContent && !isBinary && fileBytes.Length > 5000)
        {
            contentEncoding = "gzip";
            compressedBytes = _gZipStaticContentCompressor.CompressContent(fileBytes);
        }

        var cacheEntry = 
            new CachedStaticContentEntry(
                contentType, contentEncoding, isBinary, etag, compressedBytes ?? fileBytes);

        _cachedStaticContentEntries.AddOrUpdate(context.Request.Path,
            _ => cacheEntry, (_, _) => cacheEntry);

        await RespondWithContent(context, cacheEntry);

        return true;
    }
        
    private Task<bool> ReturnDefaultFile(IExecutionContext context, string requestPath)
    {
        if (requestPath == _configuration.FallBackFile)
        {
            throw new Exception("Service is misconfigured, cannot find static fall back file: " +
                                _configuration.FallBackFile);
        }
            
        return HandleRequestForPath(context, _configuration.FallBackFile!);
    }
        
    private async Task<bool> ReturnCompressedFile(IExecutionContext context, string filePath, string fileExtension, string contentEncoding)
    {
        var (contentType, isBinary) = _fileExtToMimeTypeHelper.GetMimeTypeInfo(Path.GetExtension(filePath));
        var fileBytes = await File.ReadAllBytesAsync(filePath + fileExtension);
        var etag = _etagProvider.GenerateETag(fileBytes);

        var cacheEntry =
            new CachedStaticContentEntry(
                contentType, contentEncoding, isBinary, etag, fileBytes);

        _cachedStaticContentEntries.AddOrUpdate(context.Request.Path,
            _ => cacheEntry, (_, _) => cacheEntry);

        await RespondWithContent(context, cacheEntry);

        return true;
    }
        
    private Task<bool> RespondWithContent(IExecutionContext context, CachedStaticContentEntry cacheEntry)
    {
        var etag = GetRequestETag(context);

        if (etag != StringValues.Empty && etag.Contains(cacheEntry.ETag))
        {
            context.Response.Status = (int)HttpStatusCode.NotModified;

            _configuration.OnPrepareResponse?.Invoke(context);

            return TrueComplete;
        }

        context.Response.Status = (int)HttpStatusCode.OK;
        context.Response.ContentType = cacheEntry.ContentType;
            
        _configuration.OnPrepareResponse?.Invoke(context);

        if (!string.IsNullOrEmpty(cacheEntry.ContentEncoding))
        {
            return RespondWithContentEncodedFile(context, cacheEntry);
        }

        return RespondWithStandardContent(context, cacheEntry);
    }

    private async Task<bool> RespondWithStandardContent(IExecutionContext context, CachedStaticContentEntry cacheEntry)
    {
        context.Response.IsBinary = cacheEntry.IsBinary;
        context.Response.Headers.Set(KnownHeaders.ContentLength, cacheEntry.Content.Length);

        await context.Response.Body.WriteAsync(cacheEntry.Content, 0, cacheEntry.Content.Length);

        return true;
    }

    private async Task<bool> RespondWithContentEncodedFile(IExecutionContext context, CachedStaticContentEntry cacheEntry)
    {
        if (context.Request.Headers.TryGet(KnownHeaders.AcceptEncoding, out var encoding))
        {
            if (encoding.Contains(cacheEntry.ContentEncoding))
            {
                context.Response.IsBinary = cacheEntry.IsBinary;
                context.Response.Headers.Set(KnownHeaders.ContentEncoding, "gzip");

                await context.Response.Body.WriteAsync(cacheEntry.Content, 0, cacheEntry.Content.Length);

                return true;
            }
        }
            
        return await DecompressCacheEntryToStream(context, cacheEntry);
    }

    private async Task<bool> DecompressCacheEntryToStream(IExecutionContext context, CachedStaticContentEntry cacheEntry)
    {
        using var memoryStream = _memoryStreamPool.Get();

        await memoryStream.Item.WriteAsync(cacheEntry.Content, 0 , cacheEntry.Content.Length);

        memoryStream.Item.Position = 0;

        Stream outputStream;

        if (cacheEntry.ContentEncoding == "gzip")
        {
            outputStream = new GZipStream(memoryStream.Item, CompressionMode.Decompress, true);
        }
        else if (cacheEntry.ContentEncoding == "br")
        {
            outputStream = new BrotliStream(memoryStream.Item, CompressionMode.Decompress, true);
        }
        else
        {
            throw new Exception(
                "This can only happen if a new compression type is introduced without updating this else if");
        }

        var (contentType, isBinary) = _fileExtToMimeTypeHelper.GetMimeTypeInfo(Path.GetExtension(context.Request.Path));

        context.Response.IsBinary = isBinary;

        await outputStream.CopyToAsync(context.Response.Body);
        await outputStream.DisposeAsync();

        return true;
    }

    private StringValues GetRequestETag(IExecutionContext context)
    {
        if (context.Request.Headers.TryGet(KnownHeaders.IfMatch, out var ifMatch))
        {
            return ifMatch;
        }

        return StringValues.Empty;
    }
        
    private class CachedStaticContentEntry
    {
        public CachedStaticContentEntry(string contentType, string? contentEncoding, bool isBinary, string etag, byte[] content)
        {
            ContentType = contentType;
            ContentEncoding = contentEncoding;
            IsBinary = isBinary;
            ETag = etag;
            Content = content;
        }

        public string ContentType { get; }

        public string? ContentEncoding { get; }

        public bool IsBinary { get; }

        public string ETag { get; }

        public byte[] Content { get; }
    }
}