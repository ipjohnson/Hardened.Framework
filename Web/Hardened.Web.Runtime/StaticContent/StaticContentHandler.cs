using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Hardened.Requests.Abstract.Execution;
using Hardened.Shared.Runtime.Utilities;
using Hardened.Web.Runtime.Configuration;
using Hardened.Web.Runtime.Headers;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Hardened.Web.Runtime.StaticContent
{
    public interface IStaticContentHandler
    {
        bool CanHandleRequest(IExecutionContext context);

        Task HandleRequest(IExecutionContext context);
    }

    public class StaticContentHandler : IStaticContentHandler
    {
        private readonly IStaticContentConfiguration _configuration;
        private readonly IGZipStaticContentCompressor _gZipStaticContentCompressor;
        private readonly IFileExtToMimeTypeHelper _fileExtToMimeTypeHelper;
        private readonly ConcurrentDictionary<string, string> _etags;
        private readonly bool _pathExists;

        public StaticContentHandler(
            IOptions<IStaticContentConfiguration> configuration, 
            IFileExtToMimeTypeHelper fileExtToMimeTypeHelper,
            IGZipStaticContentCompressor gZipStaticContentCompressor)
        {
            _fileExtToMimeTypeHelper = fileExtToMimeTypeHelper;
            _gZipStaticContentCompressor = gZipStaticContentCompressor;
            _configuration = configuration.Value;
            _pathExists = Directory.Exists(_configuration.Path);
            _etags = new ConcurrentDictionary<string, string>();
        }

        public bool CanHandleRequest(IExecutionContext context)
        {
            if (!_pathExists)
            {
                return false;
            }

            return false;
        }

        public Task HandleRequest(IExecutionContext context)
        {
            if (!_pathExists)
            {
                return Task.CompletedTask;
            }

            if (SuccessfulIfMatchHeader(context))
            {
                context.Response.Status = (int)HttpStatusCode.NotModified;

                return Task.CompletedTask;
            }

            var filePath = Path.Combine( _configuration.Path, context.Request.Path);

            if (File.Exists(filePath))
            {
                return ReturnFile(context, filePath);
            }

            if (File.Exists(filePath + ".gz"))
            {
                return ReturnGZipFile(context, filePath + ".gz");
            }

            if (File.Exists(filePath + ".br"))
            {
                return ReturnBrFile(context, filePath + ".br");
            }

            if (!string.IsNullOrEmpty(_configuration.FallBackFile))
            {
                return ReturnDefaultFile(context);
            }

            return Task.CompletedTask;
        }

        private Task ReturnFile(IExecutionContext context, string filePath)
        {
            throw new NotImplementedException();
        }

        private Task ReturnDefaultFile(IExecutionContext context)
        {
            throw new NotImplementedException();
        }

        private Task ReturnBrFile(IExecutionContext context, string filePath)
        {
            throw new NotImplementedException();
        }

        private Task ReturnGZipFile(IExecutionContext context, string filePath)
        {
            throw new NotImplementedException();
        }

        private bool SuccessfulIfMatchHeader(IExecutionContext context)
        {
            var requestETag = GetRequestETag(context);

            if (requestETag != StringValues.Empty)
            {
                if (_etags.TryGetValue(context.Request.Path, out var etag))
                {
                    foreach (var stringValue in requestETag)
                    {
                        if (stringValue == etag)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
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
}
