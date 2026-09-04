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
}
