using System.Text;

namespace Hardened.Requests.Runtime.Tests.Forms;

/// <summary>
/// Multipart bodies in the shapes real senders write them.
/// </summary>
internal static class MultipartBodies
{
    public const string Boundary = "rb-7c4f1e0a9d";

    public static readonly byte[] Csv = Encoding.UTF8.GetBytes(
        "id,name,category,priceCents,inStock\n1,slate-lamp-6647,tools,18928,true\n"
    );

    /// <summary>The RequestBench harness: bare boundary, quoted names, CRLF line ends.</summary>
    public static byte[] RequestBench(byte[]? file = null) =>
        Join(
            "--" + Boundary + "\r\n",
            "Content-Disposition: form-data; name=\"tenant\"\r\n\r\n",
            "qwertyuiopas\r\n",
            "--" + Boundary + "\r\n",
            "Content-Disposition: form-data; name=\"requestId\"\r\n\r\n",
            "0123456789abcdef\r\n",
            "--" + Boundary + "\r\n",
            "Content-Disposition: form-data; name=\"file\"; filename=\"forms.file.txt\"\r\n",
            "Content-Type: text/plain\r\n\r\n",
            file ?? Csv,
            "\r\n--" + Boundary + "--\r\n"
        );

    /// <summary>
    /// .NET's <c>MultipartFormDataContent</c>: a quoted boundary, bare names, <c>filename*</c>
    /// beside <c>filename</c>, and <c>Content-Type</c> ahead of <c>Content-Disposition</c>.
    /// </summary>
    public static byte[] DotNet(string boundary) =>
        Join(
            "--" + boundary + "\r\n",
            "Content-Type: text/plain; charset=utf-8\r\n",
            "Content-Disposition: form-data; name=tenant\r\n\r\n",
            "qwertyuiopas\r\n",
            "--" + boundary + "\r\n",
            "Content-Type: text/plain; charset=utf-8\r\n",
            "Content-Disposition: form-data; name=requestId\r\n\r\n",
            "0123456789abcdef\r\n",
            "--" + boundary + "\r\n",
            "Content-Type: text/plain\r\n",
            "Content-Disposition: form-data; name=file; filename=forms.file.txt; filename*=utf-8''forms.file.txt\r\n\r\n",
            Csv,
            "\r\n--" + boundary + "--\r\n"
        );

    /// <summary><c>curl -F</c>: a dashed hex boundary and quoted everything.</summary>
    public static byte[] Curl(string boundary) =>
        Join(
            "--" + boundary + "\r\n",
            "Content-Disposition: form-data; name=\"tenant\"\r\n\r\n",
            "qwertyuiopas\r\n",
            "--" + boundary + "\r\n",
            "Content-Disposition: form-data; name=\"requestId\"\r\n\r\n",
            "0123456789abcdef\r\n",
            "--" + boundary + "\r\n",
            "Content-Disposition: form-data; name=\"file\"; filename=\"forms.file.txt\"\r\n",
            "Content-Type: text/plain\r\n\r\n",
            Csv,
            "\r\n--" + boundary + "--\r\n"
        );

    public static byte[] Join(params object[] parts)
    {
        using var stream = new MemoryStream();

        foreach (var part in parts)
        {
            var bytes = part as byte[] ?? Encoding.UTF8.GetBytes((string)part);

            stream.Write(bytes, 0, bytes.Length);
        }

        return stream.ToArray();
    }
}
