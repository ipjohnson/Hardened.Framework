using Hardened.Requests.Abstract.Attributes;
using Hardened.SourceGeneration.Testing;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Web;
using Hardened.Web.Runtime.Attributes;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Tests.Infrastructure;

/// <summary>
/// Drives the request-model and binding pipeline that lives in Hardened.SourceGenerator.
///
/// <para>
/// The library ships no <c>[Generator]</c> type of its own — the wrapper projects
/// (Hardened.Web.SourceGenerator and nine siblings) compile it in via linked <c>Compile</c> items
/// and each supplies one. Referencing a wrapper here would put a second copy of every model type in
/// scope and make each use ambiguous (CS0433), so this driver calls the same
/// <see cref="WebIncrementalGenerator.Setup"/> entry point the shipped wrapper does, against the
/// assembly under test.
/// </para>
/// </summary>
public class RequestGenerator : IIncrementalGenerator {

    public void Initialize(IncrementalGeneratorInitializationContext context) {
        var applicationModel = context.SyntaxProvider.CreateSyntaxProvider(
            EntryPointSelector.UsingAttribute(),
            EntryPointSelector.TransformModel(false)
        ).WithComparer(new EntryPointSelector.Comparer());

        WebIncrementalGenerator.Setup(context, applicationModel);
    }
}

/// <summary>
/// The reference set generated request handlers bind against, and the entry point every test in
/// this suite goes through.
/// </summary>
public static class RequestGeneratorHarness {

    /// <summary>
    /// One type per assembly the generated code touches. <c>typeof</c> rather than an assembly name
    /// because it forces the assembly to load, which is what makes it resolvable.
    /// </summary>
    public static readonly Type[] Anchors = [
        typeof(GetAttribute),        // Hardened.Web.Runtime
        typeof(FromBodyAttribute),   // Hardened.Requests.Abstract

        // Fully qualified: this assembly's own namespace is Hardened.SourceGenerator.*, so an
        // unqualified Requests. or Shared. binds there instead of to the runtime packages.
        typeof(global::Hardened.Requests.Runtime.Execution.BaseExecutionHandler<>),
        typeof(global::Hardened.Shared.Runtime.Attributes.HardenedModuleAttribute)
    ];

    /// <summary>
    /// Runs the generator over one source file. Every caller is expected to finish with
    /// <see cref="GeneratorResult.AssertNoErrors"/> — see docs/testing-conventions.md §1.
    /// </summary>
    public static GeneratorResult Generate(string source) =>
        GeneratorTestHarness.Run(source, new RequestGenerator(), Anchors);

    /// <summary>Runs the generator over several source files.</summary>
    public static GeneratorResult Generate(IReadOnlyDictionary<string, string> sources) =>
        GeneratorTestHarness.Run(sources, [new RequestGenerator()], Anchors);

    /// <summary>
    /// Wraps <paramref name="body"/> in a controller with a single <c>[Get]</c> route, for the many
    /// cases where the interesting part is one handler signature.
    /// </summary>
    public static string Controller(string body) => $$"""
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Hardened.Requests.Abstract.Attributes;
        using Hardened.Requests.Abstract.Execution;
        using Hardened.Web.Runtime.Attributes;

        namespace TestApp;

        public class TestController {
        {{body}}
        }
        """;
}
