namespace Hardened1.Tests;

/// <summary>
/// How a test builds the generated client over the pipeline, once.
/// </summary>
/// <remarks>
/// A test declares <c>TemplateModuleNameClient client</c> and the harness builds one through this, over an
/// HttpClient that runs the real pipeline with no socket and already carries the test's credential.
/// The framework does not know Kiota's client type and should not; this is the only Kiota-specific
/// code outside src/Hardened1.Client. A second service is one more factory in this file.
///
/// A plain HttpClient rather than KiotaClientFactory.Create, which would wrap the handler in Kiota's
/// retry and redirect middleware - a test of a 429 or a 308 wants what the pipeline answered, not
/// what the middleware made of it.
/// </remarks>
public sealed class TemplateModuleNameClientFactory : ITestClientFactory<TemplateModuleNameClient> {

    // The base URL is required by Kiota and ignored by the handler; a code-first document with no
    // [Server] leaves it unset otherwise. Credentials are already on the HttpClient.
    public TemplateModuleNameClient Create(HttpClient http) =>
        new(new HttpClientRequestAdapter(new AnonymousAuthenticationProvider(), httpClient: http) {
            BaseUrl = "http://harness"
        });
}
