namespace Hardened.Amz.Web.Lambda.Streaming;

/// <summary>
/// Removed. Use <c>[StreamingLambdaWebModule]</c>.
///
/// <para>
/// This never worked. It is a plain attribute with no <c>[DependencyModule]</c> behind it, so it
/// registered nothing, while the streaming selector accepted it as an equal alternative to
/// <c>[StreamingLambdaWebModule]</c>. An application carrying it got a streaming bootstrap emitted
/// over an empty container and threw on construction:
/// </para>
/// <code>
/// No service for type 'Hardened.Web.Runtime.Handlers.IWebExecutionHandlerService' has been registered.
/// </code>
/// <para>
/// It was introduced in 5501a2e as the streaming opt-in, before <c>StreamingLambdaWebModule</c>
/// existed. 87ad3a5 deleted it and 64eaa60 restored it nine minutes later as a three-line stub to
/// unbreak the build, because the selector still named it. Once the module joined the selector this
/// became a strictly weaker alias of it: same code generation, no services, and nothing that can
/// only be said with this attribute.
/// </para>
/// <para>
/// An error rather than a silent removal, and rather than a warning. Dropping the name from the
/// selector without this would hand the buffered generator an application that asked for streaming
/// and produce a different transport than the source asks for - failing later, further from the
/// cause. There is no working behaviour to preserve, so naming the mistake where it is made is the
/// whole of what this type is for now. The same reasoning keeps
/// <c>ProxyIntegrationType.ApiGateway</c> in the buffered runtime.
/// </para>
/// </summary>
[Obsolete(
    "[StreamingLambdaWebApplication] never registered any services and an application using it " +
    "threw on construction. Use [StreamingLambdaWebModule], which both selects the streaming " +
    "bootstrap and registers the streaming runtime.",
    error: true)]
[AttributeUsage(AttributeTargets.Class)]
public class StreamingLambdaWebApplicationAttribute : Attribute { }
