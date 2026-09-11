using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Serializer;

public interface IContextSerializationService {
    ValueTask<T?> DeserializeRequestBody<T>(IExecutionContext context);

    Task SerializeResponse(IExecutionContext context);

    /// <summary>
    /// Writes the response through <paramref name="bound"/>, skipping the locator.
    /// </summary>
    /// <param name="bound">
    /// The serializer this handler's pipeline resolved when it was composed, or null to locate one
    /// per request.
    /// </param>
    /// <param name="declaredContentType">
    /// What the operation declared, which is what <paramref name="bound"/> was resolved for. A
    /// handler that assigns <c>Response.ContentType</c> something else overrules the binding and
    /// the locator runs, because a content type chosen per request is the one thing a decision made
    /// at build cannot know about.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>Only the locate step is skipped.</b> Everything around it - a declared output, a thrown
    /// exception, a null return, the operation's success status, a status defined to carry no body -
    /// happens either way, because none of it depends on which serializer writes.
    /// </para>
    /// <para>
    /// An operation that declares one media type, or declares none and takes the service default,
    /// is resolved once as its pipeline is built. What a request then costs is the call: no
    /// <c>Accept</c> to walk, no serializer set to search, no container resolve.
    /// </para>
    /// <para>
    /// Defaulted to the locating overload, so an implementation written before this existed keeps
    /// compiling and keeps negotiating.
    /// </para>
    /// </remarks>
    Task SerializeResponse(
        IExecutionContext context, IResponseSerializer? bound, string? declaredContentType) =>
        SerializeResponse(context);
}
