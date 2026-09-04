using Amazon.Lambda.Core;
using Amazon.Lambda.RuntimeSupport;
using DependencyModules.Runtime.Helpers;
using Hardened.Amz.Function.Lambda.Runtime.Impl;
using Hardened.Amz.SourceGeneration.Testing;
using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Shared.Runtime.Attributes;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Amz.Function.Lambda.SourceGenerator.Tests;

/// <summary>
/// The source a Lambda function application is compiled from, and the two assertions every test in
/// this project makes about what the generator did with it.
/// </summary>
public static class FunctionGeneratorHarness {

    /// <summary>
    /// One type from each assembly the generated code binds against. <c>typeof</c> rather than a
    /// name so the assembly is loaded by the time the harness collects references — a name would
    /// resolve or not depending on what ran first in the process.
    /// </summary>
    public static readonly Type[] Anchors = [
        typeof(ILambdaHandlerPackage),      // Hardened.Amz.Function.Lambda.Runtime
        typeof(LambdaBootstrapBuilder),     // Amazon.Lambda.RuntimeSupport, for the emitted Main
        typeof(IExecutionRequestHandler),   // Hardened.Requests.Abstract
        typeof(HardenedFunctionAttribute),  // Hardened.Requests.Abstract (attributes)
        typeof(HardenedModuleAttribute),    // Hardened.Shared.Runtime
        typeof(DependencyRegistry<>),       // DependencyModules.Runtime
        typeof(ILambdaContext)              // Amazon.Lambda.Core
    ];

    /// <summary>
    /// The half of the application the consumer's other generators supply, so the half under test
    /// has something to bind against.
    ///
    /// <para>
    /// <c>CreateServiceProvider</c> is normally written by the framework's
    /// <c>ServiceProviderFileGenerator</c> and is not this generator's to emit. Its signature here is
    /// that generator's, parameter for parameter, because the constructor this generator emits calls
    /// it positionally. <see cref="ServiceProviderCompositionTests"/> runs the real writer instead of
    /// this stand-in, so the claim is checked rather than assumed.
    /// </para>
    /// </summary>
    public static string Application(string members = "", string attributes = "") =>
        NamedApplication("Application", members, attributes);

    /// <inheritdoc cref="Application(string,string)"/>
    /// <param name="name">
    /// The entry point's class name. Every emitted hint name is built from it, so a test needing two
    /// applications in one compilation needs two names.
    /// </param>
    public static string NamedApplication(
        string name, string members = "", string attributes = "", string ns = "TestApp") => $$"""
        using System;
        using System.Threading.Tasks;
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
    /// Runs <see cref="LambdaFunctionSourceGenerator"/> over <paramref name="source"/> and asserts
    /// what it emitted compiles.
    /// </summary>
    public static GeneratorResult Generate(string source, params string[] additionalSources) =>
        Generate(new LambdaFunctionSourceGenerator(), source, additionalSources);

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
    /// trailing padding before a closing angle bracket, so a test pinning the exact layout would
    /// fail on a formatting change rather than a behavioural one.
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
