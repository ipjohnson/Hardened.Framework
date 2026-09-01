namespace Hardened.Requests.Abstract.Serializer;

/// <summary>
/// The response promised a representation no registered serializer can write.
/// </summary>
/// <remarks>
/// <para>
/// A configuration fault, not a client one, which is why it is not a
/// <see cref="Hardened.Requests.Abstract.Errors.StatusCodeException"/>: a service that commits to
/// <c>application/pdf</c> with no PDF serializer registered is broken however the client asked, and
/// answering 406 would make that look like the client's mistake.
/// </para>
/// <para>
/// Typed so the error path can tell this refusal apart from everything else the locator throws.
/// An error response that cannot travel as the operation's own representation is re-committed to
/// JSON and sent; a success response that cannot travel as what it promised stays a fault.
/// </para>
/// </remarks>
public class ContentTypeNotProducibleException : Exception {
    public ContentTypeNotProducibleException(string message) : base(message) { }
}
