using System.IO;
using System.IO.Compression;
using System.Text;

namespace Hardened.Generation;

/// <summary>
/// A document, compressed and written as a C# byte-array initializer.
/// </summary>
/// <remarks>
/// <para>
/// Both directions embed a document and both used to embed it as a string literal, which is the
/// expensive way: a C# string literal lives in the assembly's <c>#US</c> heap as UTF-16, so it costs
/// two bytes per ASCII character. Measured on a 279,276 byte document, built and sized three ways -
/// 562,688 bytes of assembly as a string, 92,672 gzipped and base64'd into one, and 37,376 gzipped
/// into a <c>ReadOnlySpan&lt;byte&gt;</c>. The span wins twice over base64, because base64 gives back
/// a third of what compression saved, and wins over both because the C# compiler lowers an
/// all-constant <c>byte[]</c> initializer behind a span into a metadata blob: no per-element IL, no
/// allocation, nothing copied at startup.
/// </para>
/// <para>
/// It also sidesteps the user-string limit entirely, which is what kept <c>EmbedDocument</c> off by
/// default - a large description could exceed it on its own. An RVA blob is a different heap.
/// </para>
/// <para>
/// <b>Deterministic.</b> Incremental generation and reproducible builds both require identical output
/// for identical input, and a compressor stamping a timestamp would break both while still producing
/// a valid document. <c>GZipStream</c> writes MTIME as zero, which is what makes this safe.
/// </para>
/// </remarks>
internal static class GZipLiteral {

    /// <summary>
    /// Bytes per emitted line. Long enough that the array is not thousands of lines, short enough
    /// that an editor opening the file under <c>EmitCompilerGeneratedFiles</c> is not asked to render
    /// one line of six figures.
    /// </summary>
    private const int BytesPerLine = 40;

    /// <summary>
    /// <paramref name="document"/> gzipped and written as <c>new byte[] { … };</c>.
    /// </summary>
    public static string Write(string document) => ArrayLiteral(Compress(document));

    /// <remarks>
    /// <c>Optimal</c> rather than <c>SmallestSize</c>, which netstandard2.0 does not have. On a
    /// document of this shape the two are within a few percent, and this runs inside the build.
    /// </remarks>
    public static byte[] Compress(string document) {
        var bytes = Encoding.UTF8.GetBytes(document);

        using var output = new MemoryStream();

        // Disposed before the buffer is read: GZipStream writes its footer on dispose, so a buffer
        // taken while it is still open holds a truncated member that inflates to nothing.
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true)) {
            gzip.Write(bytes, 0, bytes.Length);
        }

        return output.ToArray();
    }

    /// <summary>
    /// The array initializer, wrapped. Decimal rather than hexadecimal because it is shorter for most
    /// byte values, and this is the largest thing either generator emits.
    /// </summary>
    public static string ArrayLiteral(byte[] bytes) {
        var builder = new StringBuilder("new byte[] {");

        for (var index = 0; index < bytes.Length; index++) {
            if (index > 0) {
                builder.Append(',');
            }

            if (index % BytesPerLine == 0) {
                builder.Append("\n    ");
            }

            builder.Append(bytes[index]);
        }

        return builder.Append("\n};").ToString();
    }
}
