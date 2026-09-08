using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Aws.Lambda.Runtime.Hosting;
using Hardened.Aws.Lambda.Runtime.Streaming;
using Hardened.Requests.Runtime.DependencyInjection;
using Hardened.Shared.Runtime.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Hardened.Aws.Lambda.Runtime.Modules;

/// <summary>
/// The invocation loop, which every Lambda function needs whatever it is triggered by.
/// </summary>
/// <remarks>
/// <para>
/// Composed onto every adapter module rather than applied by an application, the way
/// <c>AspNetCoreRuntime</c> composes <c>[HardenedWebModule]</c>. An adapter with nothing to drive it
/// registers a seam nothing calls, and the failure would be at the first invocation rather than at
/// startup - so whichever trigger pulled an adapter in also pulls this.
/// </para>
/// <para>
/// <c>TryAddSingleton</c> because several adapter modules compose it and modules deduplicate by
/// equality, not by what they register.
/// </para>
/// <para>
/// <b>It brings the request pipeline with it.</b> <c>[HardenedRequestModule]</c> is what registers
/// <c>IRequestExecutor</c> and everything the chain resolves. Without it this module registered a
/// host and nothing to run, and the failure landed on the first invocation rather than at startup -
/// the identical omission <c>AspNetCoreRuntime</c> shipped with, and <c>LambdaWebModule</c> before
/// it.
/// </para>
/// </remarks>
[DependencyModule]
[HardenedRequestModule]
public partial class LambdaRuntimeModule : IServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services) {
        services.TryAddSingleton<LambdaInvocationHandler>();

        // The AWS bootstrap's stream, behind a seam. LambdaResponseStreamFactory is static and its
        // setter is internal to the AWS packages, so a test can reach a streamed response only by
        // replacing this.
        services.TryAddSingleton<IResponseStreamFactory, RuntimeResponseStreamFactory>();

        // Read from the environment at startup, and amendable from the application the way every
        // other Hardened configuration is.
        services.AddSingleton<IConfigurationPackage>(
            new SimpleConfigurationPackage(new IConfigurationValueProvider[] {
                new NewConfigurationValueProvider<ILambdaResponseModeConfiguration, LambdaResponseModeConfiguration>(
                    LambdaResponseModeConfiguration.FromEnvironment)
            }));

        services.TryAddSingleton(
            s => Options.Create(s.GetRequiredService<IConfigurationManager>()
                .GetConfiguration<ILambdaResponseModeConfiguration>()));
    }
}
