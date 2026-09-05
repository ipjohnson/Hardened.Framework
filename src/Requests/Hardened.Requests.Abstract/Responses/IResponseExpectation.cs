namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// A response type that can be rebuilt from what came back over the wire.
/// </summary>
/// <remarks>
/// <para>
/// For a test asserting through a generated client. The client hands back a status, a body and a
/// set of headers in whatever shape its generator chose - a thrown model for Kiota, a returned
/// envelope for Refit - and this turns those three into the response type the contract names, so
/// the assertion reads in the same vocabulary the handler and the document already use.
/// </para>
/// <para>
/// <b>The headers are passed because several of these carry one.</b> A 201 has its
/// <c>Location</c>, a 429 its <c>Retry-After</c>, a 401 its challenge. Each type reads the headers
/// that are its own, which is knowledge that would otherwise live in a test helper as
/// parameter-name matching: stringly typed, written once per client library, and silently wrong the
/// day a record's parameter is renamed.
/// </para>
/// <para>
/// <b>Not every response type is an expectation.</b> A non-generic error record states something
/// the handler knew and the wire does not carry back in any recoverable form -
/// <see cref="NotFound.Resource"/> names what was looked for, <see cref="Conflict.Detail"/> the
/// message. Those implement <see cref="IDeclaresStatus"/> alone, so naming one as an expectation is
/// a compile error that points at the generic form, which is the one that says what body to expect.
/// </para>
/// <para>
/// This is a test-time interface. Nothing in the request pipeline calls
/// <see cref="FromResponse"/>; a handler still constructs these directly.
/// </para>
/// </remarks>
/// <typeparam name="TSelf">The implementing type, so the factory can return it.</typeparam>
public interface IResponseExpectation<TSelf> : IDeclaresStatus where TSelf : IResponseExpectation<TSelf> {

    /// <summary>
    /// The response type, from the body the client deserialised and the headers it received.
    /// </summary>
    /// <param name="body">
    /// The deserialised body, or null where the status carries none. A type that declares a body
    /// throws rather than accept null, because a declared body that did not arrive is the defect
    /// the assertion exists to find.
    /// </param>
    /// <param name="headers">
    /// The response headers. Looked up without regard to case whatever comparer the caller's
    /// dictionary uses.
    /// </param>
    static abstract TSelf FromResponse(object? body, IReadOnlyDictionary<string, string> headers);
}
