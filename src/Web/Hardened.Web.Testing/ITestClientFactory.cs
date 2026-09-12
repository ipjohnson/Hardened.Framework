namespace Hardened.Web.Testing;

/// <summary>
/// How a test project builds a client whose constructor does not take an <see cref="HttpClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// A test parameter of a client type is built over the pipeline by one of two routes. By
/// convention, a type with a single public constructor taking exactly one <see cref="HttpClient"/>
/// is constructed with the harness's client - which is what NSwag's output and most hand-written
/// clients look like. Otherwise, by a public implementation of this interface in the test assembly,
/// found once per assembly: one method from the <see cref="HttpClient"/> the harness built, with
/// the credential already on it, to the client.
/// </para>
/// <para>
/// This is the whole of the generator-shaped seam, and it is in the test project rather than in
/// this package on purpose: a Kiota client takes an <c>IRequestAdapter</c>, and naming that type
/// here would put a generator into every test project. The template writes the three-line
/// factory; a second service is one more class in the same file.
/// </para>
/// </remarks>
public interface ITestClientFactory<out TClient> where TClient : class {

    /// <summary>Builds the client over <paramref name="http"/>, which already carries the test's credential.</summary>
    TClient Create(HttpClient http);

    /// <summary>
    /// The same, for a client that needs more than the <see cref="HttpClient"/> the harness built.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A handler of its own in front of the pipeline</b> is the case this exists for, and it was
    /// named as a reason to declare a factory before a factory could do it. The harness's client is
    /// built over a handler this package owns, that handler is not reachable from the client, and
    /// wrapping the client instead does not work: <c>HttpClient</c> marks a request as sent before
    /// the outer handler sees it, so forwarding the same message to a second client throws.
    /// <see cref="TestClientContext.CreateHttpClient"/> composes one properly, and this is what
    /// hands the context to a factory that needs it.
    /// </para>
    /// <para>
    /// Defaulted to <see cref="Create(HttpClient)"/>, so a factory that needs nothing more says
    /// nothing more and every existing one keeps working. Implement this one as well where the two
    /// differ - the harness calls this one.
    /// </para>
    /// </remarks>
    TClient Create(TestClientContext context) {
        ArgumentNullException.ThrowIfNull(context);

        return Create(context.Http);
    }
}
