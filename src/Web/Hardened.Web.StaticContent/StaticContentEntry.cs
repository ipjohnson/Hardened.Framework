using System.Globalization;
using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Web.StaticContent;

/// <summary>
/// One file, in the form it will be written.
/// </summary>
/// <remarks>
/// <para>
/// Everything a response needs is computed here, once, rather than per request: the validators for
/// both representations, the length as a string, and the coding as the <see cref="StringValues"/>
/// that goes on the wire. An entry is immutable and shared by every request for the file.
/// </para>
/// <para>
/// <b>Two validators, because there are two representations.</b> An entry stored compressed answers
/// a client that takes the coding with the stored bytes and one that does not with the same
/// resource inflated. One tag across both tells a cache holding both that they are interchangeable,
/// and it will hand a client the wrong body on the strength of it.
/// </para>
/// </remarks>
public sealed class StaticContentEntry {

    public StaticContentEntry(
        string contentType, string? contentEncoding, bool isBinary, string hash, byte[] content,
        DateTimeOffset? lastModified = null) {
        ContentType = contentType;
        ContentEncoding = contentEncoding;
        IsBinary = isBinary;
        Content = content;

        IsEncoded = !string.IsNullOrEmpty(contentEncoding);

        ContentEncodingHeader = contentEncoding switch {
            KnownEncoding.GZip => KnownEncoding.GZipStringValues,
            KnownEncoding.Br => KnownEncoding.BrStringValues,
            _ => StringValues.Empty
        };

        ETag = EntityTagHeader.Format(hash);
        EncodedETag = IsEncoded ? EntityTagHeader.Format(hash, contentEncoding!) : ETag;

        ContentLength = content.Length.ToString(CultureInfo.InvariantCulture);

        // Truncated to the second, because the header has no more precision than that. Comparing a
        // sub-second file time against a value that was rounded on the way out makes every
        // conditional request look like a miss, forever, by up to 999 milliseconds.
        LastModified = lastModified?.ToUniversalTime()
            .AddTicks(-(lastModified.Value.UtcTicks % TimeSpan.TicksPerSecond));

        LastModifiedHeader = LastModified?.ToString("R", CultureInfo.InvariantCulture);
    }

    public string ContentType { get; }

    /// <summary>The coding <see cref="Content"/> is stored in, or null when it is stored as-is.</summary>
    public string? ContentEncoding { get; }

    /// <summary>Whether <see cref="Content"/> is stored compressed.</summary>
    public bool IsEncoded { get; }

    /// <summary>The coding of <see cref="Content"/>, ready to write.</summary>
    public StringValues ContentEncodingHeader { get; }

    public bool IsBinary { get; }

    /// <summary>The validator for this resource served as stored, or inflated.</summary>
    public string ETag { get; }

    /// <summary>The validator for this resource served in <see cref="ContentEncoding"/>.</summary>
    public string EncodedETag { get; }

    public string ContentLength { get; }

    /// <summary>
    /// When the file last changed, to the second, or null when the source does not know.
    /// </summary>
    public DateTimeOffset? LastModified { get; }

    /// <summary>The same, in the one date format HTTP has. Formatted once.</summary>
    public string? LastModifiedHeader { get; }

    public byte[] Content { get; }
}
