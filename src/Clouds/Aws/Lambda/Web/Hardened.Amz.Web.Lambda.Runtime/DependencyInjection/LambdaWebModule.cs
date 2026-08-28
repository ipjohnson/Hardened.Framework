using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Amz.Shared.Lambda.Runtime;
using Hardened.Amz.Shared.Lambda.Runtime.Logging;
using Hardened.Web.Runtime.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Hardened.Amz.Web.Lambda.Runtime.DependencyInjection;

/// <summary>
/// The buffered API Gateway host, applied to an application as <c>[LambdaWebModule]</c>.
///
/// <para>
/// <c>[HardenedWebModule]</c> is what brings the web pipeline —
/// <c>IWebExecutionHandlerService</c>, the routing table and the request pipeline underneath it.
/// Without it this module registered the Lambda runtime and nothing to serve with, and the
/// constructor the web generator emits resolves <c>IWebExecutionHandlerService</c> unconditionally,
/// so every application built on it threw at construction:
/// </para>
/// <code>
/// No service for type 'Hardened.Web.Runtime.Handlers.IWebExecutionHandlerService' has been registered.
/// </code>
/// <para>
/// The failure named a framework internal rather than the missing import, and it fired before any
/// request was served, so a deployed function failed its first invocation with no route to the
/// cause. <c>LambdaFunctionModule</c> has always carried <c>[HardenedRequestModule]</c> for the
/// same reason; this is the web half of that pairing, absent until 2026-08-15.
/// <c>StreamingLambdaWebModule</c> imports this module, so it was affected too.
/// </para>
/// <para>
/// The message quoted above is what the container used to produce. Since 2026-08-27 the generated
/// bootstrap resolves through <c>ApplicationServices.Require</c>, so a missing module attribute is
/// reported by naming the attribute and the application rather than the interface.
/// </para>
/// </summary>
[DependencyModule]
[LambdaRuntimeModule]
[HardenedWebModule]
public partial class LambdaWebModule : IServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services) {
        services.RemoveAll<ILoggerProvider>();
        services.AddSingleton<ILoggerProvider, LambdaLoggerProvider>();
    }
}
