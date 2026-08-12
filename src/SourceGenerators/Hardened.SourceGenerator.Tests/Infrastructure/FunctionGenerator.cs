using Hardened.Requests.Abstract.Attributes;
using Hardened.SourceGeneration.Testing;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Tests.Infrastructure;

/// <summary>
/// Drives the function-handler pipeline that lives in Hardened.SourceGenerator.Function.
///
/// <para>
/// Same arrangement as <see cref="RequestGenerator"/>, and for the same reason: the library ships no
/// <c>[Generator]</c> type of its own, so referencing the shipped wrapper
/// (Hardened.Function.SourceGenerator) would put a second copy of every model type in scope and make
/// each use ambiguous (CS0433). This driver calls the same
/// <c>FunctionIncrementalGenerator.Setup</c> entry point the wrapper does, against the assembly under
/// test, and builds its entry-point provider identically.
/// </para>
/// </summary>
public class FunctionGenerator : IIncrementalGenerator {

    public void Initialize(IncrementalGeneratorInitializationContext context) {
        var applicationModel = context.SyntaxProvider.CreateSyntaxProvider(
            EntryPointSelector.UsingAttribute(),
            EntryPointSelector.TransformModel(false)
        ).WithComparer(new EntryPointSelector.Comparer());

        global::Hardened.SourceGenerator.Function.FunctionIncrementalGenerator.Setup(context, applicationModel);
    }
}

/// <summary>
/// The reference set generated function handlers bind against, and the source shapes every test in
/// this suite goes through.
/// </summary>
public static class FunctionGeneratorHarness {

    /// <summary>
    /// One type per assembly the generated code touches. <c>typeof</c> rather than an assembly name
    /// because it forces the assembly to load, which is what makes it resolvable.
    ///
    /// <para>
    /// No web anchor: a function handler binds against the request pipeline and the DI helpers, and
    /// nothing in Hardened.Web.Runtime. Microsoft.Extensions.DependencyInjection and
    /// DependencyModules.Runtime — which the emitted registration needs — arrive transitively
    /// through Hardened.Shared.Runtime.
    /// </para>
    /// </summary>
    public static readonly Type[] Anchors = [
        typeof(HardenedFunctionAttribute),   // Hardened.Requests.Abstract

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
        GeneratorTestHarness.Run(source, new FunctionGenerator(), Anchors);

    /// <summary>Runs the generator over several source files.</summary>
    public static GeneratorResult Generate(IReadOnlyDictionary<string, string> sources) =>
        GeneratorTestHarness.Run(sources, [new FunctionGenerator()], Anchors);

    /// <summary>
    /// An application entry point beside a handler class holding <paramref name="body"/>.
    ///
    /// <para>
    /// The entry point is what the provider stage combines against, so this is the shape that
    /// produces both outputs: one invoker per handler, plus <c>TestApplication.FunctionHandlers.cs</c>.
    /// It mirrors SqsTest, the real consumer, which is a <c>[HardenedModule] public partial class</c>
    /// beside a plain handler class.
    /// </para>
    /// </summary>
    public static string Application(string body, string extraTypes = "") => $$"""
        {{Preamble}}

        {{extraTypes}}

        [HardenedModule]
        public partial class TestApplication { }

        public class TestFunctions {
        {{body}}
        }
        """;

    /// <summary>
    /// A handler class with no application entry point, for the cases that are about the invoker
    /// alone.
    /// </summary>
    public static string Handlers(string body, string extraTypes = "") => $$"""
        {{Preamble}}

        {{extraTypes}}

        public class TestFunctions {
        {{body}}
        }
        """;

    /// <summary>A payload model and a service interface, for the binding cases.</summary>
    public const string SupportTypes = """
        public interface IThing {
            string Describe();
        }

        public class DataModel {
            public string Value { get; set; } = "";

            public int Count { get; set; }
        }
        """;

    /// <summary>
    /// Stands in for <c>Hardened.Amz.Function.Lambda.Runtime</c>'s <c>[FromContext]</c>.
    ///
    /// <para>
    /// The generator matches the attribute by its syntactic name, so a local declaration drives the
    /// same branch. The shipped attribute lives in Hardened.Amz, which Hardened.Framework cannot
    /// reference without inverting the dependency — see FunctionParameterBindingTests.
    /// </para>
    /// </summary>
    public const string FromContextAttributeDeclaration = """
        [AttributeUsage(AttributeTargets.Parameter)]
        public class FromContextAttribute : Attribute {
            public FromContextAttribute(string? name = null) {
                Name = name;
            }

            public string? Name { get; }
        }
        """;

    private const string Preamble = """
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Hardened.Requests.Abstract.Attributes;
        using Hardened.Requests.Abstract.Execution;
        using Hardened.Shared.Runtime.Attributes;

        namespace TestApp;
        """;
}
