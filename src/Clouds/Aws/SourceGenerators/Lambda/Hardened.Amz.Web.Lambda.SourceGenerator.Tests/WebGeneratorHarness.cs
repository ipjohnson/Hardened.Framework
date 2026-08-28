using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using DependencyModules.Runtime.Helpers;
using Hardened.Amz.SourceGeneration.Testing;
using Hardened.Amz.Web.Lambda.Runtime;
using Hardened.Amz.Web.Lambda.Runtime.Impl;
using Hardened.Amz.Web.Lambda.Streaming.Impl;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.Handlers;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Amz.Web.Lambda.SourceGenerator.Tests;

/// <summary>
/// The source an API Gateway application is compiled from, and the two assertions every test in this
/// project makes about what the generator did with it.
/// </summary>
public static class WebGeneratorHarness {

    /// <summary>
    /// One type from each assembly the generated code binds against. <c>typeof</c> rather than a
    /// name so the assembly is loaded by the time the harness collects references.
    /// </summary>
    public static readonly Type[] Anchors = [
        typeof(IApiGatewayEventProcessor),         // Hardened.Amz.Web.Lambda.Runtime
        typeof(LambdaWebApplicationAttribute),     // Hardened.Amz.Web.Lambda.Runtime (attributes)
        typeof(ILambdaInvokeEngine),               // Hardened.Amz.Web.Lambda.Streaming
        typeof(IWebExecutionHandlerService),       // Hardened.Web.Runtime
        typeof(IMiddlewareService),                // Hardened.Requests.Abstract
        typeof(HardenedModuleAttribute),           // Hardened.Shared.Runtime
        typeof(DependencyRegistry<>),              // DependencyModules.Runtime
        typeof(ILambdaContext),                    // Amazon.Lambda.Core
        typeof(APIGatewayHttpApiV2ProxyRequest)    // Amazon.Lambda.APIGatewayEvents
    ];

    /// <summary>
    /// The half of the application the consumer's other generators supply, so the half under test
    /// has something to bind against.
    ///
    /// <para>
    /// <c>CreateServiceProvider</c> is normally written by the framework's
    /// <c>ServiceProviderFileGenerator</c>. Its signature here is that generator's, parameter for
    /// parameter, because the constructor this generator emits calls it positionally.
    /// <see cref="ServiceProviderCompositionTests"/> runs the real writer instead of this stand-in,
    /// so the claim is checked rather than assumed.
    /// </para>
    /// </summary>
    public static string Application(string members = "", string attributes = "") =>
        NamedApplication("Application", members, attributes);

    /// <inheritdoc cref="Application(string,string)"/>
    /// <param name="name">
    /// The entry point's class name. The emitted hint name is built from it, so a test needing two
    /// applications in one compilation needs two names.
    /// </param>
    public static string NamedApplication(
        string name, string members = "", string attributes = "", string ns = "TestApp") => $$"""
        using System;
        using System.Threading.Tasks;
        using Hardened.Amz.Web.Lambda.Runtime;
        using Hardened.Shared.Runtime.Application;
        using Hardened.Shared.Runtime.Attributes;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.Logging;

        {{(string.IsNullOrEmpty(ns) ? "" : "namespace " + ns + ";")}}

        [HardenedModule]
        {{attributes}}
        public partial class {{name}} {
        {{members}}

            public ServiceProvider CreateServiceProvider(
                IHardenedEnvironment environment,
                Action<IHardenedEnvironment, IServiceCollection>? overrideDependencies,
                Action<ILoggingBuilder>? loggingBuilderAction,
                Action<IHardenedEnvironment, IServiceCollection>? initDependencies = null) {
                var services = new ServiceCollection();

                overrideDependencies?.Invoke(environment, services);
                initDependencies?.Invoke(environment, services);

                return services.BuildServiceProvider();
            }
        }
        """;

    /// <summary>
    /// Runs <see cref="WebLambdaSourceGenerator"/> over <paramref name="source"/> and asserts what it
    /// emitted compiles.
    /// </summary>
    public static GeneratorResult Generate(string source, params string[] additionalSources) =>
        Generate(new WebLambdaSourceGenerator(), source, additionalSources);

    /// <summary>
    /// Runs <paramref name="generator"/> and asserts both that its output compiles and that it did
    /// not crash.
    /// </summary>
    /// <remarks>
    /// The crash check is not redundant with <c>AssertNoErrors</c>. Every source output in this
    /// repository is wrapped by the framework's <c>SourceGeneratorWrapper</c>, which catches the
    /// exception and reports it as a <c>HardenedException</c> diagnostic at <em>Warning</em>
    /// severity. A generator that threw therefore emits nothing, reports no error, and passes a
    /// compilation that has nothing extra in it to fail.
    /// </remarks>
    public static GeneratorResult Generate(
        IIncrementalGenerator generator, string source, params string[] additionalSources) {
        var result = Run(generator, source, additionalSources);

        result.AssertNoErrors();

        AssertDidNotCrash(result);

        return result;
    }

    /// <summary>Runs the generator without asserting, for tests that assert on the diagnostics.</summary>
    public static GeneratorResult Run(
        IIncrementalGenerator generator, string source, params string[] additionalSources) =>
        Run([generator], source, additionalSources);

    /// <summary>
    /// Runs both web generators over the same compilation, the way a consumer's build does. Their
    /// selectors are written as each other's negation, so an entry point matching both would get two
    /// applications.
    /// </summary>
    public static GeneratorResult RunBoth(string source, params string[] additionalSources) =>
        Run(
            [new WebLambdaSourceGenerator(), new StreamingWebLambdaSourceGenerator()],
            source,
            additionalSources);

    /// <inheritdoc cref="Run(IIncrementalGenerator,string,string[])"/>
    public static GeneratorResult Run(
        IReadOnlyList<IIncrementalGenerator> generators, string source, params string[] additionalSources) {
        var sources = new Dictionary<string, string> { ["Test.cs"] = source };

        for (var index = 0; index < additionalSources.Length; index++) {
            sources[$"Additional{index}.cs"] = additionalSources[index];
        }

        return GeneratorTestHarness.Run(sources, generators, Anchors);
    }

    /// <summary>
    /// The attribute the streaming web selector matches, plus the one it stopped matching, declared
    /// in the test's own namespace.
    ///
    /// <para>
    /// <c>StreamingLambdaWebModuleAttribute</c> is what DependencyModules emits from the
    /// <c>[DependencyModule]</c> class the streaming runtime ships, and is the live selector. The
    /// selector matches <em>simple names in syntax</em> and never resolves a symbol, so a local
    /// declaration stands in for it — and a consumer's own attribute of that name selects the
    /// streaming generator just as effectively.
    /// </para>
    /// <para>
    /// <c>StreamingLambdaWebApplicationAttribute</c> is kept only so the test asserting it is no
    /// longer selected has something that compiles. The real type is
    /// <c>[Obsolete(error: true)]</c> as of 2026-08-27; it registered no services, so an
    /// application selected by it threw on construction.
    /// </para>
    /// </summary>
    public const string StreamingAttributes = """
        namespace TestApp;

        public class StreamingLambdaWebApplicationAttribute : System.Attribute { }

        public class StreamingLambdaWebModuleAttribute : System.Attribute { }
        """;

    /// <summary>
    /// Asserts the generator did not report the diagnostic <c>SourceGeneratorWrapper</c> raises when
    /// a writer throws.
    /// </summary>
    public static void AssertDidNotCrash(GeneratorResult result) {
        var crashes = result.GeneratorDiagnostics
            .Where(diagnostic => diagnostic.Id == "HardenedException")
            .ToArray();

        Assert.True(crashes.Length == 0,
            "the generator threw and reported it as a warning: " + string.Join(
                Environment.NewLine, crashes.Select(diagnostic => diagnostic.GetMessage())));
    }

    /// <summary>
    /// Asserts the emitted file contains <paramref name="expected"/> ignoring whitespace.
    ///
    /// <para>
    /// The emitter writes a call of more than two arguments one argument per line and leaves
    /// trailing padding before a closing angle bracket, so a test pinning the exact layout would fail
    /// on a formatting change rather than a behavioural one.
    /// </para>
    /// </summary>
    public static void AssertEmits(string source, string expected) {
        static string Compact(string value) =>
            new(value.Where(character => !char.IsWhiteSpace(character)).ToArray());

        Assert.True(Compact(source).Contains(Compact(expected), StringComparison.Ordinal),
            $"the generated file does not contain:{Environment.NewLine}  {expected}" +
            $"{Environment.NewLine}{Environment.NewLine}{source}");
    }
}
