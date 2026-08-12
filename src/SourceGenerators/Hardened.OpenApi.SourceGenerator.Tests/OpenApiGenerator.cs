using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Runtime.Validation;
using Hardened.SourceGeneration.Testing;
using Hardened.Web.Runtime.Handlers;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// Drives <see cref="OpenApiSourceGenerator"/> the way a real project does: the specification
/// arrives as an <c>AdditionalFiles</c> entry — the generator reads specs no other way — alongside
/// the C# that declares the module entry point and the <c>[Handler]</c> implementations.
/// </summary>
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
    internal static GeneratorResult Run(
        IReadOnlyDictionary<string, string> specs,
        string source,
        IReadOnlyDictionary<string, string>? buildProperties = null) =>
        GeneratorTestHarness.Run(
            new Dictionary<string, string> {
                ["GlobalUsings.cs"] = ImplicitUsings,
                ["Test.cs"] = source
            },
            [new OpenApiSourceGenerator()],
            Anchors,
            specs,
            buildProperties);

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
