using DependencyModules.Runtime.Interfaces;
using DependencyModules.Testing.Attributes.Interfaces;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.AspNetCore.Runtime;
using Hardened.Web.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hardened.Web.AspNetCore.Testing;

/// <summary>
/// Runs the test's application inside the real ASP.NET Core pipeline, on Kestrel, on a loopback
/// port the kernel picks.
/// </summary>
/// <remarks>
/// <para>
/// The real pipeline is <see cref="WebApplication"/>'s, and <see cref="WebApplication"/> builds
/// its own container. So this attribute is also the runner's container builder: the runner hands
/// it the fully populated collection, it copies every descriptor into a
/// <see cref="WebApplicationBuilder"/>, builds, and returns the application's services as the
/// test's provider. The server, the test's mocks and the test's parameters then resolve from one
/// container, which is what keeps <c>[Mock]</c> true over this host.
/// </para>
/// <para>
/// Not terminal: an unmatched path falls through to whatever the composition put behind
/// <c>UseHardened()</c> and then to ASP.NET's own 404, which is the behaviour this host exists to
/// show. The ASP.NET environment name is the Hardened one in scope, <c>test</c> by default, so
/// <c>[EnvironmentName("development")]</c> means one thing to <c>[IfEnvironment]</c> and to
/// <c>app.Environment.IsDevelopment()</c> in a composition.
/// </para>
/// <para>
/// A test project already carrying an <c>IServiceProviderBuilderAttribute</c> of its own conflicts
/// with this one; the runner takes the narrowest.
/// </para>
/// </remarks>
public sealed class AspNetCoreHostAttribute : TestHostAttribute, IDependencyModuleProvider, IServiceProviderBuilderAttribute {
    private readonly Type _composition;

    /// <summary>The default composition: <c>app.UseHardened()</c> alone.</summary>
    public AspNetCoreHostAttribute() : this(typeof(DefaultAspNetCoreTestComposition)) {
    }

    /// <param name="composition">
    /// A public <see cref="IAspNetCoreTestComposition"/> with a parameterless constructor, which
    /// arranges the pipeline the way <c>Program.cs</c> does.
    /// </param>
    public AspNetCoreHostAttribute(Type composition) {
        ArgumentNullException.ThrowIfNull(composition);

        _composition = composition;
    }

    public Type Composition => _composition;

    public IDependencyModule GetModule() => new AspNetCoreRuntime();

    /// <remarks>
    /// The host is also registered as itself, as an instance, so <see cref="BuildServiceProvider"/>
    /// can find it in the collection without state on this attribute. The instance registration
    /// is never disposed by the container; the <c>ITestHost</c> factory registration
    /// <c>[WebTesting]</c> makes is what is.
    /// </remarks>
    public override ITestHost CreateHost(ITestMethodContext testMethod, IServiceCollection services) {
        if (!typeof(IAspNetCoreTestComposition).IsAssignableFrom(_composition) ||
            _composition.GetConstructor(Type.EmptyTypes) == null) {
            throw new InvalidOperationException(
                $"{_composition.FullName} is named as the composition of [AspNetCoreHost], and it is not a " +
                "public IAspNetCoreTestComposition with a parameterless constructor.");
        }

        var environment = testMethod.Attributes.OfType<EnvironmentNameAttribute>().LastOrDefault()?.Name ?? "test";
        var host = new AspNetCoreTestHost(
            (IAspNetCoreTestComposition)Activator.CreateInstance(_composition)!, environment);

        services.AddSingleton(host);

        return host;
    }

    /// <remarks>
    /// The runner asks the narrowest container builder in scope, and <c>[WebTesting]</c> creates
    /// the narrowest host in scope, and the two need not agree: a method carrying
    /// <c>[PipelineHost]</c> inside a class carrying this attribute has this attribute building
    /// the container for a pipeline host. The host in the collection decides. This attribute's
    /// own host means a <see cref="WebApplication"/>; any other host means the plain container
    /// the runner would have built; no host at all means <c>[WebTesting]</c> is missing.
    /// </remarks>
    public IServiceProvider BuildServiceProvider(ITestMethodContext testMethod, IServiceCollection serviceCollection) {
        var host = serviceCollection
            .Select(descriptor => descriptor.ImplementationInstance as AspNetCoreTestHost)
            .FirstOrDefault(instance => instance != null);

        if (host == null) {
            if (!serviceCollection.Any(descriptor => descriptor.ServiceType == typeof(ITestHost))) {
                throw new InvalidOperationException(
                    "[AspNetCoreHost] builds the container for a test [WebTesting] registered a host in, and this " +
                    "test has none: declare [assembly: WebTesting] beside the entry point attribute.");
            }

            return serviceCollection.BuildServiceProvider();
        }

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions {
            ApplicationName = testMethod.Method.DeclaringType!.Assembly.GetName().Name,
            EnvironmentName = host.EnvironmentName,
        });

        // Port 0, the way the socket tests in this repository bind: the kernel picks, and the
        // bound address is read back from app.Urls once the server has started.
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        // The test's logger provider arrives with the copy below; the host's console and debug
        // providers do not belong in a test run.
        builder.Logging.ClearProviders();

        host.Composition.Configure(builder);

        // The test's container on top of the host's: last wins for a single resolution, and the
        // host's own registrations stay for what the test did not name.
        foreach (var descriptor in serviceCollection) {
            builder.Services.Add(descriptor);
        }

        var app = builder.Build();

        host.Attach(app);

        return app.Services;
    }
}
