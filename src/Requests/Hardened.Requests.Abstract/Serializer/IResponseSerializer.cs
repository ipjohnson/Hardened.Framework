using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Serializer;

/// <summary>
/// Where a response serializer sits relative to the others. Lower runs first, matching
/// <c>ExecutionFilterOrder</c>.
/// </summary>
/// <remarks>
/// <para>
/// Order exists because the alternative is registration order, and registration order here is not
/// something an application can control: within a module, DependencyModules sorts by whether a
/// registration is conditional and then by implementation type name, so which serializer won came
/// down to how two classes sorted alphabetically. Renaming one changed which one handled a request.
/// </para>
/// <para>
/// Values are spaced so a serializer can be slotted between two of them without renumbering.
/// </para>
/// </remarks>
public enum ResponseSerializerOrder {
    /// <summary>
    /// Rendered output. Ahead of everything because a response that names a view is asking for that
    /// view specifically, and would otherwise be taken by whichever serializer matched the request's
    /// <c>Accept</c> - which, for a browser, is usually the JSON one.
    /// </summary>
    Template = -1000,

    /// <summary>A serializer for one specific media type, ahead of the general-purpose ones.</summary>
    Specialized = -100,

    /// <summary>The default, and where the JSON serializers sit.</summary>
    Normal = 0,

    /// <summary>
    /// Behind the general-purpose serializers. A serializer here answers only when a client asked
    /// for its media type specifically, and never for one that expressed no preference.
    /// </summary>
    /// <remarks>
    /// Where <c>RawResponseSerializer</c> sits. Ahead of JSON it would have made every handler
    /// returning a bare string answer <c>text/plain</c> to a client sending no <c>Accept</c> - which
    /// is most of them, and is what half of this repository's own routing and binding tests do. That
    /// is the ASP.NET Core convention, but it is not this framework's existing behaviour and the
    /// change would reach every application returning a string from anything.
    /// </remarks>
    Deferred = 1000
}

public interface IResponseSerializer {
    bool IsDefaultSerializer { get; }

    /// <summary>
    /// Lower is tested first. Defaults to <see cref="ResponseSerializerOrder.Normal"/>, so an
    /// existing serializer that does not care keeps working unchanged.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="IsDefaultSerializer"/> on purpose. Order decides who is asked first;
    /// <c>IsDefaultSerializer</c> decides who answers when nobody claims the context at all. A
    /// specialist sitting ahead of JSON must not stop JSON being the fallback for
    /// <c>Accept: */*</c>, which is the most common request shape there is.
    /// </remarks>
    int Order => (int)ResponseSerializerOrder.Normal;

    /// <summary>
    /// Whether this serializer can write <paramref name="context"/>'s response as
    /// <paramref name="mediaType"/>.
    /// </summary>
    /// <param name="mediaType">
    /// One entry from the client's <c>Accept</c> header, or a content type the response has already
    /// committed to. May be a wildcard - use <see cref="MediaType.Matches"/> rather than comparing
    /// it directly.
    /// </param>
    /// <remarks>
    /// <para>
    /// Two questions in one, and both belong here: does this serializer emit that media type, and
    /// can it handle this particular response value. A template serializer answers no for a response
    /// that names no view however well the media type matches.
    /// </para>
    /// <para>
    /// This replaced <c>CanProcessContext(context)</c>, which asked a serializer to decide its own
    /// capability by reading <c>Request.Accept</c> - so a serializer's answer depended on what the
    /// client asked for, which is backwards, and every implementation had to rank itself against
    /// serializers it could not see. The framework now walks the client's preferences in order and
    /// asks about one media type at a time, so ranking lives in one place and this method only has
    /// to answer about itself.
    /// </para>
    /// </remarks>
    bool CanProduce(string mediaType, IExecutionContext context);

    Task SerializeResponse(IExecutionContext context);
}
