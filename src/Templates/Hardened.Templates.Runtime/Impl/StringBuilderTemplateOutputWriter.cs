using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using System.Text;
using Hardened.Templates.Abstract;
using System.IO.Compression;

namespace Hardened.Templates.Runtime.Impl;

public class StringBuilderTemplateOutputWriter : ITemplateOutputWriter
{
    private readonly StringBuilder _stringBuilder;
    private readonly Stream? _outputStream;

    public StringBuilderTemplateOutputWriter(
        StringBuilder stringBuilder,
        Stream? outputStream = null)
    {
        _stringBuilder = stringBuilder;
        _outputStream = outputStream;
    }

    public void Write(object? text)
    {
        if (text != null)
        {
            if (text is SafeString safeString)
            {
                _stringBuilder.Append(safeString);
            }
            else
            {
                _stringBuilder.Append(EscapeService?.EscapeString(text.ToString()) ?? "");
            }
        }
    }

    public void WriteRaw(object? text)
    {
        if (text != null)
        {
            _stringBuilder.Append(text);
        }
    }

    public async Task FlushWriter(IExecutionContext executionContext)
    {
        if (_outputStream != null)
        {
            var canCompressResponse = CanCompressResponse(executionContext);
            var outputBuffer = Encoding.UTF8.GetBytes(_stringBuilder.ToString());
            
            if (canCompressResponse && 
                outputBuffer.Length > 1000)
            {
                executionContext.Response.Headers.Set(KnownHeaders.ContentEncoding, KnownEncoding.GZipStringValues);
                
                await using var compressStream = new GZipStream(_outputStream, CompressionLevel.Fastest, true);

                await compressStream.WriteAsync(outputBuffer, 0, outputBuffer.Length);
                await compressStream.FlushAsync();
            }
            else
            {
                await _outputStream.WriteAsync(outputBuffer, 0, outputBuffer.Length);
            }
            
            await _outputStream.FlushAsync();
        }
    }

    public IStringEscapeService? EscapeService { get; set; }
    
    private bool CanCompressResponse(IExecutionContext context)
    {
        return context.Request.Headers.TryGet(KnownHeaders.AcceptEncoding, out var header) &&
               header.Contains(KnownEncoding.GZip);
    }
}