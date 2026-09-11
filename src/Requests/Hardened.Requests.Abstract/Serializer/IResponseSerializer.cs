using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Serializer;

public interface IResponseSerializer {
    bool IsDefaultSerializer { get; }

    /// <summary>
    /// The media type this serializer writes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The tag a serializer is registered and located under, so selection is a lookup rather than a
    /// search. An operation declares what it produces, the pipeline resolves the serializer for that
    /// type once as it is built, and a request costs neither.
    /// </para>
    /// <para>
    /// This replaced <c>int Order</c>. Order existed to adjudicate two serializers claiming one
    /// media type, because registration order within a module is decided by how implementation type
    /// names sort. A tag makes that a registration conflict with a stated rule - the last
    /// registration under a type wins, so an application's own serializer beats the framework's -
    /// rather than a race between two class names.
    /// </para>
    /// </remarks>
    string ContentType { get; }

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
    /// Defaulted in terms of <see cref="ContentType"/>, which is the whole answer for a serializer
    /// that writes one media type. Override it only where the question is about the response value
    /// rather than the type: <c>RawResponseSerializer</c> writes bytes it is handed and nothing
    /// else, and <c>StreamingJsonResponseSerializer</c> answers only for a response the streaming
    /// filter committed.
    /// </para>
    /// <para>
    /// Reached only on the negotiated path now. An operation that declares one media type is bound
    /// to its serializer as the handler's pipeline is composed and never asks this.
    /// </para>
    /// </remarks>
    bool CanProduce(string mediaType, IExecutionContext context) =>
        MediaType.Matches(mediaType, ContentType);

    Task SerializeResponse(IExecutionContext context);
}
