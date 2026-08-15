using Microsoft.CodeAnalysis;

namespace Hardened.Amz.Web.Lambda.SourceGenerator;

public static class WebLambdaDiagnostics {

    /// <summary>
    /// <c>[LambdaWebApplication(Version = ProxyIntegrationType.ApiGateway)]</c> — REST API, payload
    /// format 1.0 — on an entry point this generator can only emit an HTTP API v2 handler for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Version</c> was never read. Both web generators emit an <c>IApiGatewayV2Handler</c> taking
    /// an <c>APIGatewayHttpApiV2ProxyRequest</c> unconditionally, so selecting <c>ApiGateway</c>
    /// produced a v2 handler that a REST API would feed a v1 payload. The two formats differ in
    /// almost every field a request is read from — v1 has <c>httpMethod</c> and <c>path</c> at the
    /// top level where v2 has <c>requestContext.http</c> — so the handler deserialized to a request
    /// with a null <c>RequestContext.Http</c> and failed on the first property read, at runtime, in
    /// a deployed function.
    /// </para>
    /// <para>
    /// An error rather than a warning: there is no partially-working outcome to preserve. The knob
    /// is offered in the README's quick start, so the likeliest way to reach it is by copying the
    /// documented example, and a build failure is the only thing that reaches someone doing that.
    /// </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor RestApiIntegrationNotSupported = new(
        "HRDAWS001",
        "API Gateway REST API integration is not implemented",
        "'{0}' selects ProxyIntegrationType.ApiGateway, which is not implemented. This generator " +
        "emits an HTTP API (payload format 2.0) handler, and a REST API sends payload format 1.0 — " +
        "the handler would deserialize the request to nulls and fail at runtime. Use " +
        "ProxyIntegrationType.HttpApiV2 and put an HTTP API in front of the function.",
        "Hardened.Amz.Lambda",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
