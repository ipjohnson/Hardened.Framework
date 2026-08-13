using System.Diagnostics;
using Hardened.Benchmarks.AspNetSut;
using Hardened.Benchmarks.AspNetSut.Controllers;
using Hardened.Benchmarks.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hardened.Benchmarks.Infrastructure;

public enum AspNetFlavor {
    MinimalApi,
    Mvc
}

/// <summary>
/// An ASP.NET Core request pipeline with no server behind it.
///
/// <c>ApplicationBuilder</c> is used directly rather than <c>WebApplication</c> because
/// <c>WebApplication.Build</c> installs template middleware that is not part of what is being
/// compared. Registering the middleware explicitly means the pipeline under measurement is
/// exactly <c>UseRouting</c> plus <c>UseEndpoints</c> and nothing else, which is the closest
/// ASP.NET equivalent to what <c>UseHardened</c> installs on the Hardened side.
///
/// Each flavor gets its own provider. Sharing one would make the two pipelines share endpoint
/// data sources and matcher caches, which is exactly the kind of cross-contamination that
/// produces a plausible number for the wrong thing.
/// </summary>
public sealed class AspNetHarness : IPipelineHarness {
    private readonly ServiceProvider _provider;
    private readonly RequestDelegate _pipeline;

    public string Name { get; }

    public AspNetHarness(AspNetFlavor flavor, bool sourceGeneratedJson) {
        Name = (flavor == AspNetFlavor.MinimalApi ? "aspnet-minimal" : "aspnet-mvc")
            + (sourceGeneratedJson ? "-srcgen" : "");

        var services = new ServiceCollection();

        services.AddLogging(builder => builder.ClearProviders());
        services.AddRouting();
        services.AddTransient<ISumService, SumService>();

        // EndpointRoutingMiddleware takes a DiagnosticListener as a constructor dependency. The
        // generic host normally registers it; without a host, building the pipeline throws.
        // Registered as a real listener rather than a stub so the middleware pays its usual
        // IsEnabled checks, which is what it would do in production with no subscribers attached.
        var diagnosticListener = new DiagnosticListener("Microsoft.AspNetCore");
        services.AddSingleton(diagnosticListener);
        services.AddSingleton<DiagnosticSource>(diagnosticListener);

        if (flavor == AspNetFlavor.Mvc) {
            var mvc = services.AddControllers()
                // MVC discovers controllers from the entry assembly, which here is the benchmark
                // host rather than the SUT. Without this the routing table is empty and every
                // request 404s while still looking like a successful, very fast benchmark.
                .AddApplicationPart(typeof(BenchmarkMvcController).Assembly);

            if (sourceGeneratedJson) {
                mvc.AddJsonOptions(options =>
                    options.JsonSerializerOptions.TypeInfoResolverChain.Insert(
                        0, BenchmarkJsonContext.Default));
            }
        }
        else if (sourceGeneratedJson) {
            services.ConfigureHttpJsonOptions(options =>
                options.SerializerOptions.TypeInfoResolverChain.Insert(
                    0, BenchmarkJsonContext.Default));
        }

        _provider = services.BuildServiceProvider();

        if (sourceGeneratedJson) {
            AssertSourceGenJsonApplied(flavor);
        }

        var builder = new ApplicationBuilder(_provider);

        builder.UseRouting();
        builder.UseEndpoints(endpoints => {
            if (flavor == AspNetFlavor.Mvc) {
                endpoints.MapControllers();
            }
            else {
                endpoints.MapBenchmarkEndpoints();
            }
        });

        _pipeline = builder.Build();
    }

    /// <summary>
    /// Confirms the source-generated resolver actually reached the options the pipeline will use.
    ///
    /// The response-body check in <c>PipelineVerification</c> cannot catch this: reflection and
    /// source generation produce identical JSON, so an option that silently failed to apply would
    /// pass verification while making the whole serialization axis a duplicate of the reflection
    /// run. Given that the two currently measure the same, it matters that the reason is "they
    /// genuinely cost the same" rather than "the setting never took".
    /// </summary>
    private void AssertSourceGenJsonApplied(AspNetFlavor flavor) {
        var resolvers = flavor == AspNetFlavor.Mvc
            ? _provider.GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>()
                .Value.JsonSerializerOptions.TypeInfoResolverChain
            : _provider.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>()
                .Value.SerializerOptions.TypeInfoResolverChain;

        if (!resolvers.Any(resolver => resolver is BenchmarkJsonContext)) {
            throw new InvalidOperationException(
                $"{flavor} was configured for source-generated JSON, but BenchmarkJsonContext is " +
                "not in the resolver chain. This benchmark would silently duplicate the " +
                "reflection run.");
        }
    }

    public IServiceProvider Provider => _provider;

    public async Task<int> Execute(RequestScenario scenario, MemoryStream responseBody) {
        using var scope = _provider.CreateScope();

        var context = HttpContextFactory.Create(scenario, scope.ServiceProvider, responseBody);

        await _pipeline(context);

        return context.Response.StatusCode;
    }

    public void Dispose() => _provider.Dispose();
}
