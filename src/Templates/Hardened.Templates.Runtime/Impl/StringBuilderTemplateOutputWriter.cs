using System.Text;
using Hardened.Templates.Abstract;

namespace Hardened.Templates.Runtime.Impl
{
    public class StringBuilderTemplateOutputWriter : ITemplateOutputWriter
    {
        private readonly StringBuilder _stringBuilder;
        private readonly Stream? _outputStream;
        public StringBuilderTemplateOutputWriter(StringBuilder stringBuilder, Stream? outputStream = null)
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

        public async Task FlushWriter()
        {
            if (_outputStream != null)
            {
                var outputBuffer = Encoding.UTF8.GetBytes(_stringBuilder.ToString());

                await _outputStream.WriteAsync(outputBuffer, 0, outputBuffer.Length);
                await _outputStream.FlushAsync();
            }
        }

        public IStringEscapeService? EscapeService { get; set; }
    }
}
