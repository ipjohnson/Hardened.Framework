using System.Text;
using Hardened.Templates.Abstract;
using System.IO.Compression;

namespace Hardened.Templates.Runtime.Impl;

public class StringBuilderTemplateOutputWriter : ITemplateOutputWriter
{
    private readonly StringBuilder _stringBuilder;
    private readonly Stream? _outputStream;
    private readonly bool _canCompressResponse;

    public StringBuilderTemplateOutputWriter(
        StringBuilder stringBuilder,
        Stream? outputStream = null,
        bool canCompressResponse = false)
    {
        _stringBuilder = stringBuilder;
        _outputStream = outputStream;
        _canCompressResponse = canCompressResponse;
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

    public async Task FlushWriter()
    {
        if (_outputStream != null)
        {
            var outputBuffer = Encoding.UTF8.GetBytes(_stringBuilder.ToString());
            
            if (_canCompressResponse && 
                outputBuffer.Length > 1000)
            {
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
}