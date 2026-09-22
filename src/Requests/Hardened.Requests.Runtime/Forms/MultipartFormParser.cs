using System.Text;
using Hardened.Requests.Abstract.Forms;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Runtime.Forms;

/// <summary>
/// Parses a buffered <c>multipart/form-data</c> body (RFC 7578 over RFC 2046) into its fields and
/// files.
/// </summary>
/// <remarks>
/// <para>
/// <b>Over a buffer the reader already holds.</b> A field that follows a file has to bind before
/// the handler runs, so the whole body is read first whatever order the parts arrive in, and each
/// file is a slice of that buffer rather than a copy of it.
/// </para>
/// <para>
/// <b>Three senders' shapes, all read the same.</b> A bare or quoted boundary; a quoted or bare
/// <c>name</c>; <c>Content-Type</c> before or after <c>Content-Disposition</c>. That covers a
/// browser, <c>curl -F</c> and .NET's <c>MultipartFormDataContent</c>, which quotes its boundary
/// and sends <c>filename*</c> beside <c>filename</c>. <c>filename*</c> is ignored, because RFC 7578
/// section 4.2 says a sender must not use it.
/// </para>
/// </remarks>
internal static class MultipartFormParser
{
    public const int MaxBoundaryLength = 70;

    public const int MaxHeaderBytes = 16 * 1024;

    public const int MaxParts = 1024;

    private static readonly byte[] Crlf = "\r\n"u8.ToArray();

    private static readonly byte[] HeaderEnd = "\r\n\r\n"u8.ToArray();

    /// <summary>
    /// The <c>boundary</c> parameter of a content type, unquoted, or null when it has none of a
    /// length RFC 2046 allows.
    /// </summary>
    public static string? Boundary(string? contentType)
    {
        if (contentType == null)
        {
            return null;
        }

        foreach (var (name, value) in Parameters(contentType))
        {
            if (name.Equals("boundary", StringComparison.OrdinalIgnoreCase))
            {
                return value.Length is > 0 and <= MaxBoundaryLength ? value : null;
            }
        }

        return null;
    }

    /// <summary>The form <paramref name="body"/> carries.</summary>
    /// <exception cref="FormatException">The body is not a multipart body under this boundary.</exception>
    public static MultipartFormCollection Parse(ArraySegment<byte> body, string boundary)
    {
        var fields = new Dictionary<string, StringValues>(StringComparer.Ordinal);
        var files = new Dictionary<string, List<IFormFile>>(StringComparer.Ordinal);

        var span = body.AsSpan();
        var dashBoundary = Encoding.ASCII.GetBytes("--" + boundary);
        var delimiter = Encoding.ASCII.GetBytes("\r\n--" + boundary);

        // The first boundary opens the body, or follows a preamble that is ignored.
        var start = span.StartsWith(dashBoundary) ? 0 : span.IndexOf(delimiter);

        if (start < 0)
        {
            throw new FormatException("The body does not contain its boundary.");
        }

        var position = start + (start == 0 ? dashBoundary.Length : delimiter.Length);
        var parts = 0;

        while (true)
        {
            var rest = span.Slice(position);

            // "--" straight after a delimiter closes the body. Anything after it is epilogue.
            if (rest.StartsWith("--"u8))
            {
                break;
            }

            // Transport padding, then the CRLF that ends the boundary line.
            var lineEnd = rest.IndexOf(Crlf);

            if (lineEnd < 0 || rest.Slice(0, lineEnd).Trim(" \t"u8).Length != 0)
            {
                throw new FormatException("A boundary line carries something other than padding.");
            }

            position += lineEnd + Crlf.Length;
            rest = span.Slice(position);

            if (++parts > MaxParts)
            {
                throw new FormatException("The body has more than " + MaxParts + " parts.");
            }

            int headersLength;
            int contentStart;

            // A part with no headers at all starts with the blank line.
            if (rest.StartsWith(Crlf))
            {
                headersLength = 0;
                contentStart = position + Crlf.Length;
            }
            else
            {
                var headerEnd = rest.IndexOf(HeaderEnd);

                if (headerEnd < 0 || headerEnd > MaxHeaderBytes)
                {
                    throw new FormatException(
                        "A part's headers do not end, or run past " + MaxHeaderBytes + " bytes."
                    );
                }

                headersLength = headerEnd;
                contentStart = position + headerEnd + HeaderEnd.Length;
            }

            var headers = Encoding.UTF8.GetString(span.Slice(position, headersLength));
            var contentLength = span.Slice(contentStart).IndexOf(delimiter);

            if (contentLength < 0)
            {
                throw new FormatException("A part does not end with the boundary.");
            }

            AddPart(headers, body.Slice(contentStart, contentLength), fields, files);

            position = contentStart + contentLength + delimiter.Length;
        }

        return new MultipartFormCollection(fields, files);
    }

    private static void AddPart(
        string headers,
        ArraySegment<byte> content,
        Dictionary<string, StringValues> fields,
        Dictionary<string, List<IFormFile>> files
    )
    {
        string? name = null;
        string? fileName = null;

        // RFC 7578 section 4.4: a part that states no type is text/plain.
        var contentType = "text/plain";

        foreach (var line in headers.Split("\r\n"))
        {
            var colon = line.IndexOf(':');

            if (colon <= 0)
            {
                continue;
            }

            var header = line.Substring(0, colon).Trim();
            var value = line.Substring(colon + 1).Trim();

            if (header.Equals("Content-Disposition", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var (parameter, parameterValue) in Parameters(value))
                {
                    if (parameter.Equals("name", StringComparison.OrdinalIgnoreCase))
                    {
                        name = parameterValue;
                    }
                    else if (parameter.Equals("filename", StringComparison.OrdinalIgnoreCase))
                    {
                        fileName = parameterValue;
                    }
                }
            }
            else if (header.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                contentType = value;
            }
        }

        if (name == null)
        {
            throw new FormatException("A part has no Content-Disposition name.");
        }

        if (fileName != null)
        {
            if (!files.TryGetValue(name, out var named))
            {
                files[name] = named = new List<IFormFile>();
            }

            named.Add(new FormFile(name, fileName, contentType, content));

            return;
        }

        var text = Encoding.UTF8.GetString(content);

        fields[name] = fields.TryGetValue(name, out var existing)
            ? StringValues.Concat(existing, text)
            : new StringValues(text);
    }

    /// <summary>
    /// The parameters after the first <c>;</c> of a header value, with quoted values unquoted.
    /// </summary>
    private static IEnumerable<(string Name, string Value)> Parameters(string headerValue)
    {
        var index = headerValue.IndexOf(';');

        while (index >= 0 && index < headerValue.Length)
        {
            index++;

            while (index < headerValue.Length && headerValue[index] is ' ' or '\t')
            {
                index++;
            }

            var equals = headerValue.IndexOf('=', index);

            if (equals < 0)
            {
                yield break;
            }

            var name = headerValue.Substring(index, equals - index).Trim();
            var valueStart = equals + 1;
            string value;
            int next;

            if (valueStart < headerValue.Length && headerValue[valueStart] == '"')
            {
                var builder = new StringBuilder();
                var i = valueStart + 1;

                for (; i < headerValue.Length && headerValue[i] != '"'; i++)
                {
                    // A quoted-pair: the backslash is dropped and the character after it kept.
                    if (headerValue[i] == '\\' && i + 1 < headerValue.Length)
                    {
                        i++;
                    }

                    builder.Append(headerValue[i]);
                }

                value = builder.ToString();
                next = headerValue.IndexOf(';', Math.Min(i + 1, headerValue.Length));
            }
            else
            {
                next = headerValue.IndexOf(';', valueStart);
                value = (
                    next < 0
                        ? headerValue.Substring(valueStart)
                        : headerValue.Substring(valueStart, next - valueStart)
                ).Trim();
            }

            yield return (name, value);

            index = next;
        }
    }
}
