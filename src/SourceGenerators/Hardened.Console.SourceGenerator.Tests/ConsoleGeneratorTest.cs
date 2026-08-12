using Hardened.Commands.Attributes;
using Hardened.SourceGeneration.Testing;
using Microsoft.CodeAnalysis;

namespace Hardened.Console.SourceGenerator.Tests;

/// <summary>
/// Shared setup for the console generator suite.
///
/// <para>
/// A real console application is built by three generators working together:
/// <c>Hardened.Library.SourceGenerator</c> emits <c>CreateServiceProvider</c>,
/// <c>Hardened.DependencyModules.SourceGenerator</c> emits the module plumbing, and
/// <c>Hardened.Console.SourceGenerator</c> — the one under test — emits the entry point's
/// constructors, <c>Run()</c>, the command binders and the command definition provider.
/// </para>
///
/// <para>
/// Running all three here would put two copies of <c>EntryPointSelector</c> and friends in scope,
/// because each generator project compiles <c>Hardened.SourceGenerator/Shared/**</c> in via linked
/// <c>Compile</c> items, and every use becomes ambiguous (CS0433). So the other two generators'
/// contribution is hand-written below instead, and the console generator's output is compiled
/// against it. That is the honest test: it fails exactly when what this generator writes stops
/// fitting what the others provide.
/// </para>
/// </summary>
public static class ConsoleGeneratorTest {

    /// <summary>
    /// One type per assembly the generated code binds against. <c>typeof</c> rather than a name so
    /// the assembly is loaded by the time references are collected.
    /// </summary>
    private static readonly Type[] Anchors = [
        typeof(CommandAttribute),                                  // Hardened.Commands
        typeof(Shared.Runtime.Attributes.HardenedModuleAttribute), // Hardened.Shared.Runtime
        typeof(Microsoft.Extensions.DependencyInjection.ServiceCollection)
    ];

    /// <summary>
    /// What <c>Hardened.Library.SourceGenerator</c> would have emitted, plus the global using the
    /// generated binders rely on.
    ///
    /// <para>
    /// <c>System.Linq</c> is a <c>global using</c> here rather than a file-scoped one on purpose:
    /// the emitted binder calls <c>FirstOrDefault()</c> without importing <c>System.Linq</c>, so it
    /// only compiles in a project with <c>ImplicitUsings</c> enabled. Every Hardened console project
    /// has it, which is why nobody has noticed, but a consumer who turns implicit usings off gets a
    /// build error out of generated code. Recorded here rather than papered over silently.
    /// </para>
    /// </summary>
    public const string EntryPointSupport =
        """
        global using System.Linq;

        using Hardened.Shared.Runtime.Application;

        namespace TestApp;

        public partial class Application {
            public System.IServiceProvider? CreateServiceProvider(
                IHardenedEnvironment environment,
                object? overrideDependencies,
                object? loggingBuilderAction) => null;
        }
        """;

    /// <summary>A <c>Program.cs</c> that drives the entry point the way a real one does.</summary>
    public const string Program =
        """
        using TestApp;

        var application = new Application(args);

        var result = await application.Run();

        await application.DisposeAsync();

        return result;
        """;

    /// <summary>
    /// Compiles <paramref name="commands"/> as a console application together with everything the
    /// generated entry point needs, runs the console generator, and returns what it produced.
    /// </summary>
    public static GeneratorResult Generate(string commands) =>
        Generate(new Dictionary<string, string> { ["Commands.cs"] = commands });

    /// <inheritdoc cref="Generate(string)"/>
    public static GeneratorResult Generate(IReadOnlyDictionary<string, string> commandFiles) =>
        GeneratorTestHarness.Run(
            commandFiles
                .Concat(new Dictionary<string, string> {
                    ["EntryPointSupport.cs"] = EntryPointSupport,
                    ["Program.cs"] = Program
                })
                .ToDictionary(pair => pair.Key, pair => pair.Value),
            [new ConsoleSourceGenerator()],
            Anchors,
            outputKind: OutputKind.ConsoleApplication);

    /// <summary>
    /// Compiles <paramref name="commands"/> as a library. Use this when the case under test is about
    /// what the generator writes rather than about the entry point being a runnable program.
    /// </summary>
    public static GeneratorResult GenerateLibrary(string commands) =>
        GeneratorTestHarness.Run(
            new Dictionary<string, string> {
                ["Commands.cs"] = commands,
                ["EntryPointSupport.cs"] = EntryPointSupport
            },
            [new ConsoleSourceGenerator()],
            Anchors);

    /// <summary>The entry point declaration every command source below is compiled alongside.</summary>
    public const string ApplicationDeclaration =
        """
        using Hardened.Commands;
        using Hardened.Commands.Attributes;
        using Hardened.Shared.Runtime.Attributes;
        using System.Threading.Tasks;

        namespace TestApp;

        [HardenedModule]
        public partial class Application { }
        """;

    /// <summary>Wraps <paramref name="body"/> in the standard entry point and usings.</summary>
    public static string WithApplication(string body) =>
        ApplicationDeclaration + Environment.NewLine + Environment.NewLine + body;

    /// <summary>
    /// Generates from <paramref name="commands"/>, asserts the result compiles, and returns the
    /// emitted entry point with every run of whitespace collapsed to a single space.
    ///
    /// <para>
    /// Assertions on emitted source should fail when the generator writes the wrong thing, not when
    /// it writes the right thing with different indentation. Collapsing whitespace lets an expected
    /// fragment be written on one line and still match the multi-line output.
    /// </para>
    /// </summary>
    public static string EmittedEntryPoint(string commands) =>
        Whitespace.Replace(
            Generate(commands).AssertNoErrors().SourceContaining("Application.Commands"),
            " ");

    private static readonly System.Text.RegularExpressions.Regex Whitespace = new(@"\s+");
}
