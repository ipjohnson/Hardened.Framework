using System.Reflection;
using Hardened.Web.Testing;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;

namespace Hardened.Kiota.Testing;

/// <summary>
/// Builds a Kiota client over the pipeline, and reads what a call through one answered. Named by
/// <see cref="KiotaTestingAttribute"/>.
/// </summary>
/// <remarks>
/// <para>
/// A Kiota client is a <see cref="BaseRequestBuilder"/> taking one <see cref="IRequestAdapter"/>,
/// which is a shape rather than a name - so this recognises every client a generation produces,
/// and a client written against the same shape by hand.
/// </para>
/// <para>
/// <b>A plain handler chain rather than <c>KiotaClientFactory</c>.</b> The factory's chain carries
/// Kiota's retry and redirect middleware, and a test of a 429 or a 308 wants to see what the
/// pipeline answered rather than what the middleware made of it. What is in the chain is one
/// handler of this package's own, recording what the client received, which is where
/// <c>Returns&lt;T&gt;()</c> reads the status and the headers of a response the client did not
/// throw on.
/// </para>
/// <para>
/// Authentication is anonymous at the Kiota layer because the test's credential is already on the
/// <see cref="HttpClient"/>, applied by the harness. A client needing a real
/// <see cref="IAuthenticationProvider"/> - one under test in its own right - declares an
/// <see cref="ITestClientFactory{TClient}"/>, which wins over this.
/// </para>
/// </remarks>
public sealed class KiotaClientRoute : ITestClientRoute, ITestClientReader {

    private const string UntypedNote =
        "The client threw a bare ApiException rather than a model, which is what it does for a " +
        "status the document declares no body for - so there was nothing for it to deserialise " +
        "into, whatever the response carried.";

    public bool CanBuild(Type clientType) =>
        clientType is { IsClass: true, IsAbstract: false } &&
        typeof(BaseRequestBuilder).IsAssignableFrom(clientType) &&
        AdapterConstructor(clientType) != null;

    public object Build(TestClientContext context, Type clientType) {
        ArgumentNullException.ThrowIfNull(context);

        var constructor = AdapterConstructor(clientType)
            ?? throw new InvalidOperationException(
                $"{clientType.FullName} is not a Kiota client: it has no public constructor taking " +
                "one IRequestAdapter.");

        var adapter = new HttpClientRequestAdapter(
            new AnonymousAuthenticationProvider(),
            httpClient: context.CreateHttpClient(new RecordingHandler())) {

            // Kiota resolves every request against this, and a code-first document with no server
            // entry leaves the generated client with none. The transport ignores the host.
            BaseUrl = context.BaseAddress.ToString().TrimEnd('/'),
        };

        return constructor.Invoke([adapter]);
    }

    /// <summary>
    /// What the client reported: from the exception where it threw one, from the recorded response
    /// where it did not.
    /// </summary>
    /// <remarks>
    /// The two halves come from different places on purpose. A refusal's status and headers are
    /// read off the thrown model, which is what the client surfaced - reading them from the
    /// recording instead would pass a test whose client dropped them. A success has nothing to
    /// read them from, because the generated method returns the body alone. Kiota throws the
    /// generated model for a status the document declares a body for and <see cref="ApiException"/>
    /// itself for one it does not, so the exact type is the answer to whether there is a body at all.
    /// </remarks>
    public Task<ClientAnswer?> Read(object? result, Exception? thrown, Type? bodyType) {
        if (thrown is ApiException refusal) {
            var untyped = refusal.GetType() == typeof(ApiException);

            return Task.FromResult<ClientAnswer?>(new ClientAnswer(
                refusal.ResponseStatusCode,
                untyped ? null : refusal,
                Flatten(refusal.ResponseHeaders),
                untyped ? UntypedNote : null));
        }

        if (thrown == null && RecordingHandler.TryCurrent(out var received)) {
            return Task.FromResult<ClientAnswer?>(new ClientAnswer(received.Status, result, received.Headers));
        }

        return Task.FromResult<ClientAnswer?>(null);
    }

    public string Unreadable =>
        "A Kiota client built through [assembly: KiotaTesting] records what it receives, and no " +
        "call through one has been answered in this test; a client built by an ITestClientFactory " +
        "of the test project's own, or constructed by hand, is not recorded.";

    private static ConstructorInfo? AdapterConstructor(Type clientType) {
        foreach (var constructor in clientType.GetConstructors()) {
            var parameters = constructor.GetParameters();

            if (parameters.Length == 1 && parameters[0].ParameterType == typeof(IRequestAdapter)) {
                return constructor;
            }
        }

        return null;
    }

    private static IReadOnlyDictionary<string, string> Flatten(IDictionary<string, IEnumerable<string>>? headers) {
        var flattened = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (headers == null) {
            return flattened;
        }

        foreach (var header in headers) {
            flattened[header.Key] = string.Join(", ", header.Value);
        }

        return flattened;
    }
}
