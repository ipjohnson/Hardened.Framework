namespace Hardened.Amz.Web.Lambda.Runtime;

public enum ProxyIntegrationType {
    /// <summary>
    /// API Gateway REST API, payload format 1.0. <b>Not implemented</b> — selecting it is a build
    /// error (HRDAWS001).
    ///
    /// <para>
    /// It was accepted and ignored until 2026-08-15: the generator emits an HTTP API v2 handler
    /// regardless, so a REST API in front of it sent a payload format the handler could not read,
    /// and the function failed at runtime on a null <c>RequestContext.Http</c>. The member is kept
    /// rather than deleted so that the mistake is named where it is made, instead of becoming an
    /// unresolved symbol in code that used to compile.
    /// </para>
    /// </summary>
    ApiGateway,

    /// <summary>
    /// API Gateway HTTP API, payload format 2.0. The only implemented integration, and the default.
    /// </summary>
    HttpApiV2
}

/// <summary>
/// Marks an application as an API Gateway-hosted Lambda.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class LambdaWebApplicationAttribute : Attribute {
    public ProxyIntegrationType Version { get; set; } = ProxyIntegrationType.HttpApiV2;
}