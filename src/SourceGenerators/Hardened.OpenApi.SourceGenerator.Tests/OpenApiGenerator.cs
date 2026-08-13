using Hardened.OpenApi.SourceGenerator.Emitters;
using Hardened.OpenApi.SourceGenerator.Models;
using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Runtime.Validation;
using Hardened.SourceGeneration.Testing;
using Hardened.Web.Runtime.Handlers;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// Drives <see cref="OpenApiSourceGenerator"/> the way a real project does: the build task parses
/// the specification and writes a normalised model, and that model — not the yaml — arrives as an
/// <c>AdditionalFiles</c> entry alongside the C# declaring the entry point and the
/// <c>[Handler]</c> implementations.
/// </summary>
/// <remarks>
/// Callers still pass yaml, because a test that reads as a specification is worth more than one
/// that reads as a serialised model. The parse-and-serialise step the build task performs happens
/// here instead, which also means these tests exercise the round trip on every run rather than
/// only where <c>SpecModelSerializerTests</c> looks.
/// </remarks>
internal static class OpenApiGenerator {

    /// <summary>
    /// One type from every assembly the emitted code binds against. Without these the generated
    /// trees fail to compile for want of a reference, and the failure reads as a generator defect.
    /// </summary>
    private static readonly Type[] Anchors = [
        typeof(HandlerAttribute),                        // Hardened.Requests.Abstract
        typeof(ValidationFilter),                        // Hardened.Requests.Runtime
        typeof(IWebExecutionRequestHandlerProvider)      // Hardened.Web.Runtime
    ];

    /// <summary>The hint name the generator emits on every run whatever the input.</summary>
    internal const string DiagnosticHintName = "_OpenApiDiagnostic.g.cs";

    /// <summary>Runs the generator over one specification and the supplied C#.</summary>
    internal static GeneratorResult Run(
        string spec,
        string source = MinimalEntryPoint,
        string specFileName = "petstore.yaml",
        IReadOnlyDictionary<string, string>? buildProperties = null) =>
        Run(
            new Dictionary<string, string> { [specFileName] = spec },
            source,
            buildProperties);

    /// <summary>Runs the generator over several specifications at once.</summary>
    /// <remarks>
    /// Both halves of the pipeline, because that is what a project compiles. The task's emitted
    /// types arrive as ordinary source files - which is exactly what they are once MSBuild has put
    /// them in <c>@(Compile)</c> - and the models arrive as additional files.
    /// </remarks>
    internal static GeneratorResult Run(
        IReadOnlyDictionary<string, string> specs,
        string source,
        IReadOnlyDictionary<string, string>? buildProperties = null) {
        // Must resolve exactly as the generator does, and against the same defaults the harness
        // supplies. The two halves emit into one namespace and bind across it, so a harness that
        // resolves differently produces a handler referencing a service interface that was emitted
        // somewhere else entirely. Keys carry no build_property. prefix here - the harness adds it.
        var excludeFromCoverage =
            !string.Equals(Property(buildProperties, "ExcludeGeneratedCodeFromCoverage"), "false", StringComparison.OrdinalIgnoreCase);

        var ns = FirstNonEmpty(
            Property(buildProperties, "HardenedOpenApiNamespace"),
            Property(buildProperties, "RootNamespace"),
            // The default TestAnalyzerConfigOptions supplies for RootNamespace when a test sets none.
            "TestNamespace");

        var sources = new Dictionary<string, string> {
            ["GlobalUsings.cs"] = ImplicitUsings,
            ["Test.cs"] = source
        };

        var models = new Dictionary<string, string>();
        var taskEmitted = new Dictionary<string, string>();

        foreach (var spec in specs) {
            var model = ToSpecModel(spec.Key, spec.Value);
            models[ModelFileNameFor(spec.Key)] = model;

            var emitted = TaskEmittedSource(model, ns, excludeFromCoverage);
            sources[emitted.Key] = emitted.Value;
            taskEmitted[emitted.Key] = emitted.Value;
        }

        var result = GeneratorTestHarness.Run(sources, [new OpenApiSourceGenerator()], Anchors, models, buildProperties);

        // GeneratedSources carries both halves, because that is what the project ends up compiling.
        // Splitting them would make every assertion depend on which side of the task/generator line
        // a given type happens to fall on today - and that line moves. Where the distinction is the
        // point, a test should be in Hardened.OpenApi.BuildTask.Tests instead.
        var combined = new Dictionary<string, string>(taskEmitted, StringComparer.Ordinal);

        foreach (var generated in result.GeneratedSources) {
            combined[generated.Key] = generated.Value;
        }

        return new GeneratorResult(
            combined,
            result.GeneratorDiagnostics,
            result.CompilationDiagnostics,
            result.Compilation,
            result.GeneratorExceptions,
            result.DuplicateHintNames);
    }

    private static string? Property(IReadOnlyDictionary<string, string>? properties, string key) =>
        properties is not null && properties.TryGetValue(key, out var value) ? value : null;

    private static string FirstNonEmpty(params string?[] candidates) =>
        candidates.FirstOrDefault(candidate => !string.IsNullOrEmpty(candidate)) ?? "Generated";

    /// <summary>
    /// The single file <c>ExtractOpenApiSpec</c> writes into <c>@(Compile)</c> for one spec, built
    /// by the same composer the task calls rather than by a copy of it.
    /// </summary>
    internal static KeyValuePair<string, string> TaskEmittedSource(
        string specModel, string ns, bool excludeFromCoverage) {
        var model = SpecModelSerializer.Read(specModel);

        return new KeyValuePair<string, string>(
            $"{model.FileName}.g.cs", SpecFileEmitter.Emit(model, ns, excludeFromCoverage));
    }

    /// <summary>
    /// Runs the generator over additional files exactly as given, with no parse step.
    /// </summary>
    /// <remarks>
    /// For the cases where the point is what the generator does with a file it did not expect - a
    /// corrupt model, an unrelated additional file. Everything else should go through
    /// <see cref="Run(string, string, string, IReadOnlyDictionary{string, string}?)"/> and pass a
    /// specification.
    /// </remarks>
    internal static GeneratorResult RunRaw(
        IReadOnlyDictionary<string, string> additionalFiles,
        string source = MinimalEntryPoint,
        IReadOnlyDictionary<string, string>? buildProperties = null) =>
        GeneratorTestHarness.Run(
            new Dictionary<string, string> {
                ["GlobalUsings.cs"] = ImplicitUsings,
                ["Test.cs"] = source
            },
            [new OpenApiSourceGenerator()],
            Anchors,
            additionalFiles,
            buildProperties);

    /// <summary>
    /// What the build task does, inline: parse the yaml once and hand the generator the normalised
    /// model. A spec that will not parse is surfaced here rather than as an empty generator run,
    /// which is the same trade the task makes.
    /// </summary>
    private static string ToSpecModel(string specFileName, string yaml) {
        var fileName = Path.GetFileNameWithoutExtension(specFileName);

        var model = OpenApiSpecParser.Parse(yaml, fileName, CancellationToken.None)
            ?? throw new InvalidOperationException($"'{specFileName}' did not parse; the generator would see nothing.");

        // The task names the resolver and records it in the model; the generator is told rather than
        // deriving it, so the harness has to do the naming too or the routing table registers a type
        // nothing emitted.
        model.JsonTypeInfoResolverName = JsonTypeInfoEmitter.ResolverNameFor(fileName);

        return SpecModelSerializer.Write(model);
    }

    private static string ModelFileNameFor(string specFileName) =>
        Path.GetFileNameWithoutExtension(specFileName) + ".openapi-model.txt";

    /// <summary>
    /// What <c>&lt;ImplicitUsings&gt;enable&lt;/ImplicitUsings&gt;</c> puts in every .NET 8 project.
    ///
    /// <para>
    /// Not optional decoration. <c>ServiceInterfaceEmitter</c> writes <c>Task&lt;List&lt;Pet&gt;&gt;</c>
    /// and <c>ValidationFilterEmitter</c> writes <c>IEnumerable&lt;RequestFilterInfo&gt;</c> without
    /// emitting <c>using System.Threading.Tasks;</c> or <c>using System.Collections.Generic;</c>, so
    /// the generated code only compiles in a project with implicit usings on. Every project in this
    /// repository enables them, which is why nothing has noticed. A raw
    /// <c>CSharpCompilation</c> has none, so the harness supplies them the way the SDK would.
    /// </para>
    /// </summary>
    private const string ImplicitUsings =
        """
        global using global::System;
        global using global::System.Collections.Generic;
        global using global::System.IO;
        global using global::System.Linq;
        global using global::System.Net.Http;
        global using global::System.Threading;
        global using global::System.Threading.Tasks;
        """;

    /// <summary>
    /// The smallest project the generator will produce a routing table for: a partial class
    /// carrying <c>[HardenedModule]</c>, which is what <c>EntryPointSelector</c> looks for.
    /// </summary>
    internal const string MinimalEntryPoint =
        """
        using Hardened.Shared.Runtime.Attributes;

        namespace TestNamespace;

        [HardenedModule]
        public partial class TestApp {
        }
        """;

    /// <summary>
    /// The entry point plus a <c>[Handler]</c> implementing a generated service interface. This is
    /// the shape a real consumer writes, and the one that exercises interface → implementation
    /// registration in the routing table.
    /// </summary>
    internal static string EntryPointWithHandler(string handlerBody) =>
        $$"""
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Hardened.Requests.Abstract.Attributes;
        using Hardened.Shared.Runtime.Attributes;
        using TestNamespace.Models;
        using TestNamespace.Services;

        namespace TestNamespace;

        [HardenedModule]
        public partial class TestApp {
        }

        {{handlerBody}}
        """;
}
