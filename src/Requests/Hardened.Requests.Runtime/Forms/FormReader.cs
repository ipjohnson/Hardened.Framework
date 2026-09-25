using System.Buffers;
using System.Text;
using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Forms;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Runtime.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ValidationModules;

namespace Hardened.Requests.Runtime.Forms;

/// <summary>
/// Reads an <c>application/x-www-form-urlencoded</c> or <c>multipart/form-data</c> body into its
/// fields and files.
/// </summary>
/// <remarks>
/// <para>
/// On <c>IKnownServices</c> rather than as a member of <c>IExecutionRequest</c>, and that is the
/// whole reason the binding costs what it does. A <c>Form</c> property on the request would be a
/// change to the contract every transport implements and the conformance suite covers.
/// <c>IKnownServices</c> has one implementation, resolved from the container, so every host gets
/// this without knowing it happened.
/// </para>
/// <para>
/// A singleton, so what one request read is kept in its scoped <see cref="RequestFormCache"/>
/// rather than here. The second read of a request's form returns the first.
/// </para>
/// <para>
/// <b>A form body is read forward-only, into memory, up to a cap.</b> Nothing reads the stream's
/// <c>Position</c> or <c>Length</c> or the <c>Content-Length</c> header: four of the hosts hand
/// over a stream that cannot seek, and the decompression filter removes the header. A body past
/// <see cref="IFormConfiguration.MaxBodyBytes"/> answers 413, whichever kind of form it is. One
/// that does not parse, including one with more than 1,024 fields or parts, answers the 400 a
/// malformed JSON body gets.
/// </para>
/// </remarks>
[SingletonService(Using = RegistrationType.Try)]
public class FormReader : IFormReader
{
    private long? _maxBodyBytes;

    public async ValueTask<IFormCollection> ReadForm(IExecutionContext context)
    {
        var contentType = context.Request.ContentType;

        // No content type at all reads as url-encoded, which is what it has always done.
        var urlEncoded =
            string.IsNullOrEmpty(contentType)
            || IsMediaType(contentType, KnownContentType.FormUrlEncoded);

        if (!urlEncoded && !IsMediaType(contentType, KnownContentType.MultipartFormData))
        {
            return EmptyFormCollection.Instance;
        }

        var body = context.Request.Body;

        if (body == null!)
        {
            return EmptyFormCollection.Instance;
        }

        var cache = context.RequestServices.GetService<RequestFormCache>();

        if (cache?.Form is { } cached)
        {
            return cached;
        }

        IFormCollection form;
        byte[]? pooled = null;

        if (urlEncoded)
        {
            form = await ReadUrlEncoded(context, body).ConfigureAwait(false);
        }
        else
        {
            (form, pooled) = await ReadMultipart(context, body, contentType!, cache != null)
                .ConfigureAwait(false);
        }

        cache?.Keep(form, pooled);

        return form;
    }

    /// <summary>
    /// Whether a request's content type is <paramref name="mediaType"/>, whatever parameters it
    /// carries.
    /// </summary>
    /// <remarks>
    /// Compared without its parameters. <c>MediaType.Matches</c> strips them only from the type it
    /// produces, so a multipart body, which always carries a boundary, never matched - and nor did
    /// <c>application/x-www-form-urlencoded; charset=UTF-8</c>, which jQuery and axios send, and
    /// which read as an empty form.
    /// </remarks>
    private static bool IsMediaType(string? contentType, string mediaType)
    {
        var type = contentType.AsSpan();
        var parameters = type.IndexOf(';');

        if (parameters >= 0)
        {
            type = type.Slice(0, parameters);
        }

        return type.Trim().Equals(mediaType, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether a request's content type is one a form is read from, whatever its parameters.
    /// </summary>
    internal static bool IsFormContentType(string contentType) =>
        IsMediaType(contentType, KnownContentType.FormUrlEncoded)
        || IsMediaType(contentType, KnownContentType.MultipartFormData);

    /// <summary>
    /// The url-encoded body, read to the cap a multipart body is read to, and parsed.
    /// </summary>
    /// <remarks>
    /// Always pooled. Nothing keeps the bytes, because the fields are strings by the time this
    /// returns.
    /// </remarks>
    private async Task<IFormCollection> ReadUrlEncoded(IExecutionContext context, Stream body)
    {
        // From the start, because a filter ahead of this one may have read it. RetryFilter already
        // rewinds a seekable body between attempts for the same reason.
        if (body.CanSeek)
        {
            body.Position = 0;
        }

        var (buffer, length) = await ReadAll(
                body,
                MaxBodyBytes(context),
                pool: true,
                context.CancellationToken
            )
            .ConfigureAwait(false);

        string text;

        try
        {
            text = Text(buffer, length);
        }
        finally
        {
            Release(buffer, pool: true);
        }

        // Put it back for whatever reads next. A handler binding both form fields and a body model
        // is reported at build time, but a filter reading the body is not something the generator
        // can see.
        if (body.CanSeek)
        {
            body.Position = 0;
        }

        try
        {
            return UrlEncodedParser.Parse(text);
        }
        catch (FormatException exception)
        {
            throw Invalid(exception.Message, exception);
        }
    }

    /// <summary>The body as UTF-8 text.</summary>
    /// <remarks>A byte order mark is not part of the first field's name.</remarks>
    private static string Text(byte[] buffer, int length)
    {
        var bytes = new ReadOnlySpan<byte>(buffer, 0, length);

        if (bytes.StartsWith(Encoding.UTF8.Preamble))
        {
            bytes = bytes.Slice(Encoding.UTF8.Preamble.Length);
        }

        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// The multipart body, parsed, and the pooled buffer its files are slices of.
    /// </summary>
    /// <remarks>
    /// Pooled only where the request has a cache to give the buffer back when it ends. Without one
    /// nothing could return it safely, since the files still point into it.
    /// </remarks>
    private async Task<(IFormCollection Form, byte[]? Pooled)> ReadMultipart(
        IExecutionContext context,
        Stream body,
        string contentType,
        bool pool
    )
    {
        var boundary =
            MultipartFormParser.Boundary(contentType)
            ?? throw Invalid("The multipart body's content type has no usable boundary.");

        if (body.CanSeek)
        {
            body.Position = 0;
        }

        var (buffer, length) = await ReadAll(
                body,
                MaxBodyBytes(context),
                pool,
                context.CancellationToken
            )
            .ConfigureAwait(false);

        try
        {
            var form = MultipartFormParser.Parse(
                new ArraySegment<byte>(buffer, 0, length),
                boundary
            );

            return (form, pool ? buffer : null);
        }
        catch (FormatException exception)
        {
            Release(buffer, pool);

            throw Invalid(exception.Message, exception);
        }
    }

    private static async Task<(byte[] Buffer, int Length)> ReadAll(
        Stream body,
        long limit,
        bool pool,
        CancellationToken cancellationToken
    )
    {
        // An array cannot be longer than this, so a larger configured cap is this one in practice.
        var cap = (int)Math.Min(limit, Array.MaxLength - 1);
        var buffer = Allocate(Math.Min(16 * 1024, cap + 1), pool);
        var length = 0;

        try
        {
            while (true)
            {
                if (length == buffer.Length)
                {
                    var larger = Allocate(
                        (int)Math.Min((long)buffer.Length * 2, (long)cap + 1),
                        pool
                    );

                    buffer.AsSpan(0, length).CopyTo(larger);
                    Release(buffer, pool);
                    buffer = larger;
                }

                var read = await body.ReadAsync(buffer.AsMemory(length), cancellationToken)
                    .ConfigureAwait(false);

                if (read == 0)
                {
                    return (buffer, length);
                }

                length += read;

                if (length > cap)
                {
                    throw new FormBodyTooLargeException(limit);
                }
            }
        }
        catch
        {
            Release(buffer, pool);

            throw;
        }
    }

    private static byte[] Allocate(int size, bool pool) =>
        pool ? ArrayPool<byte>.Shared.Rent(size) : new byte[size];

    private static void Release(byte[] buffer, bool pool)
    {
        if (pool)
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private long MaxBodyBytes(IExecutionContext context) =>
        _maxBodyBytes ??=
            context
                .RootServiceProvider.GetService<IOptions<IFormConfiguration>>()
                ?.Value.MaxBodyBytes
            ?? FormConfiguration.DefaultMaxBodyBytes;

    /// <summary>
    /// The refusal a malformed JSON body gets, so a malformed form reads the same however it was
    /// caught.
    /// </summary>
    private static Validation.ValidationException Invalid(string message, Exception? inner = null)
    {
        var result = ValidationResult.FromErrors(
            new[] { new ValidationError("body", ValidationCodes.Invalid, message) }
        );

        return inner == null
            ? new Validation.ValidationException(result)
            : new Validation.ValidationException(result, inner);
    }
}
