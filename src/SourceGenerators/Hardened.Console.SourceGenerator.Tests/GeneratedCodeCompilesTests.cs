using Xunit;

namespace Hardened.Console.SourceGenerator.Tests;

/// <summary>
/// Every case here ends in <see cref="SourceGeneration.Testing.GeneratorResult.AssertNoErrors"/>,
/// which compiles the input together with the generated trees.
///
/// <para>
/// <c>Hardened.Console.SourceGenerator</c> had no test of any kind before 2026-08-11 — the assembly
/// measured 0% line and 0% branch coverage. It emits the console application's entry point:
/// three constructors, <c>Run()</c>, <c>DisposeAsync()</c>, a binder per command and the command
/// definition provider. All of it is code a consumer's build depends on, and none of it was ever
/// compiled by a test. See TESTING-PLAN.md §2.1 for the three defects this class of test exists to
/// catch.
/// </para>
/// </summary>
public class GeneratedCodeCompilesTests {

    /// <summary>
    /// The whole point: the generated entry point is a program, so it is compiled as one.
    /// <c>OutputKind.ConsoleApplication</c> means the compiler also checks the generated
    /// <c>Application(string[])</c> constructor and <c>Run()</c> are reachable from a real
    /// <c>Program.cs</c> — a library compilation would not.
    /// </summary>
    [Fact]
    public void TheGeneratedEntryPointCompilesAsAConsoleApplication() {
        ConsoleGeneratorTest.Generate(ConsoleGeneratorTest.WithApplication(
            """
            [Command("greet", Description = "Say hello")]
            public class GreetCommand {
                public string Name { get; set; } = "";
            }

            public class GreetHandler : ICommandHandler<GreetCommand> {
                public Task<int> Handle(GreetCommand value) => Task.FromResult(0);
            }
            """)).AssertNoErrors();
    }

    /// <summary>
    /// An application with no commands at all. The generator still has to emit a usable entry
    /// point, and a <c>foreach</c> over an empty collection is exactly the shape that produces an
    /// empty <c>new CommandOption[]{ }</c> or a method with no body statements.
    /// </summary>
    [Fact]
    public void AnApplicationWithNoCommandsStillCompiles() {
        ConsoleGeneratorTest.Generate(ConsoleGeneratorTest.ApplicationDeclaration).AssertNoErrors();
    }

    [Fact]
    public void ACommandWithNoOptionsCompiles() {
        ConsoleGeneratorTest.Generate(ConsoleGeneratorTest.WithApplication(
            """
            [Command("version")]
            public class VersionCommand { }

            public class VersionHandler : ICommandHandler<VersionCommand> {
                public Task<int> Handle(VersionCommand value) => Task.FromResult(0);
            }
            """)).AssertNoErrors();
    }

    /// <summary>
    /// A subcommand deriving from its parent command. The generator emits a binder that resolves
    /// the parent's binder and delegates to it, so this is the shape that has to keep two generic
    /// type arguments straight.
    /// </summary>
    [Fact]
    public void ASubcommandDerivingFromItsParentCompiles() {
        ConsoleGeneratorTest.Generate(ConsoleGeneratorTest.WithApplication(
            """
            [Command("math", Description = "Math commands")]
            public class MathCommand {
                public int X { get; set; }
                public int Y { get; set; }
            }

            [Command("add", ParentCommand = "math", Description = "Add two numbers")]
            public class AddCommand : MathCommand { }

            public class AddHandler : ICommandHandler<AddCommand> {
                public Task<int> Handle(AddCommand value) => Task.FromResult(0);
            }
            """)).AssertNoErrors();
    }

    /// <summary>
    /// Two subcommands under one parent. Binder class names are built from the parent type name and
    /// the command type name, so this is where a naming collision would surface as CS0102.
    /// </summary>
    [Fact]
    public void SiblingSubcommandsCompileWithoutBinderNameCollisions() {
        ConsoleGeneratorTest.Generate(ConsoleGeneratorTest.WithApplication(
            """
            [Command("math")]
            public class MathCommand {
                public int X { get; set; }
                public int Y { get; set; }
            }

            [Command("add", ParentCommand = "math")]
            public class AddCommand : MathCommand { }

            [Command("subtract", ParentCommand = "math")]
            public class SubtractCommand : MathCommand { }

            public class AddHandler : ICommandHandler<AddCommand> {
                public Task<int> Handle(AddCommand value) => Task.FromResult(0);
            }

            public class SubtractHandler : ICommandHandler<SubtractCommand> {
                public Task<int> Handle(SubtractCommand value) => Task.FromResult(0);
            }
            """)).AssertNoErrors();
    }

    /// <summary>
    /// The binder emits <c>Convert&lt;T&gt;</c> with the property's type as the generic argument, so
    /// every property type a consumer might use has to produce a call that binds.
    /// </summary>
    [Theory]
    [InlineData("string")]
    [InlineData("int")]
    [InlineData("long")]
    [InlineData("double")]
    [InlineData("decimal")]
    [InlineData("bool")]
    [InlineData("System.Guid")]
    [InlineData("System.DateTime")]
    public void EveryScalarOptionTypeCompiles(string propertyType) {
        ConsoleGeneratorTest.Generate(ConsoleGeneratorTest.WithApplication(
            $$"""
            [Command("run")]
            public class RunCommand {
                public {{propertyType}} Value { get; set; } = default!;
            }

            public class RunHandler : ICommandHandler<RunCommand> {
                public Task<int> Handle(RunCommand value) => Task.FromResult(0);
            }
            """)).AssertNoErrors();
    }

    /// <summary>
    /// A nullable reference type property. The generated binder assigns
    /// <c>Convert&lt;string?&gt;(...)</c>, and the harness compiles with nullable enabled, so a
    /// mismatch here is a real warning-as-error for a consumer.
    /// </summary>
    [Fact]
    public void ANullableOptionCompiles() {
        ConsoleGeneratorTest.Generate(ConsoleGeneratorTest.WithApplication(
            """
            [Command("run")]
            public class RunCommand {
                public string? Value { get; set; }
            }

            public class RunHandler : ICommandHandler<RunCommand> {
                public Task<int> Handle(RunCommand value) => Task.FromResult(0);
            }
            """)).AssertNoErrors();
    }

    /// <summary>
    /// Three levels of command nesting. The parser only walks two, but the generator has no such
    /// limit and a consumer can write it, so it has to emit code that builds.
    /// </summary>
    [Fact]
    public void ThreeLevelsOfNestingCompile() {
        ConsoleGeneratorTest.Generate(ConsoleGeneratorTest.WithApplication(
            """
            [Command("db")]
            public class DbCommand {
                public string Connection { get; set; } = "";
            }

            [Command("migrate", ParentCommand = "db")]
            public class MigrateCommand : DbCommand {
                public string Target { get; set; } = "";
            }

            [Command("up", ParentCommand = "migrate")]
            public class UpCommand : MigrateCommand { }

            public class UpHandler : ICommandHandler<UpCommand> {
                public Task<int> Handle(UpCommand value) => Task.FromResult(0);
            }
            """)).AssertNoErrors();
    }

    /// <summary>
    /// Commands in a namespace other than the entry point's. The generated file is written into the
    /// entry point's namespace and refers to command types by their global name, so this is where a
    /// missing <c>global::</c> qualification would show up.
    /// </summary>
    [Fact]
    public void ACommandInAnotherNamespaceCompiles() {
        ConsoleGeneratorTest.Generate(new Dictionary<string, string> {
            ["Application.cs"] = ConsoleGeneratorTest.ApplicationDeclaration,
            ["Report.cs"] =
                """
                using Hardened.Commands;
                using Hardened.Commands.Attributes;
                using System.Threading.Tasks;

                namespace Reporting;

                [Command("report")]
                public class ReportCommand {
                    public string Format { get; set; } = "";
                }

                public class ReportHandler : ICommandHandler<ReportCommand> {
                    public Task<int> Handle(ReportCommand value) => Task.FromResult(0);
                }
                """
        }).AssertNoErrors();
    }

    /// <summary>
    /// A command carrying <c>[Option]</c>, <c>[FileOption]</c> and <c>[ExcludeOption]</c>. The three
    /// attributes ship in <c>Hardened.Commands</c> and a consumer can apply them, so the generated
    /// code has to compile in their presence.
    ///
    /// <para>
    /// It does — because the generator ignores all three. <c>GetNameAndDescription</c>,
    /// <c>GetDefaultAndRequired</c> and <c>GetOptionTypeAndArray</c> return constants and never read
    /// an attribute, so the option name is always the lowercased property name, the description is
    /// always empty, every option is required, and the type is always <c>String</c>. This test
    /// asserts only what is true — that a consumer using them can build. See TESTING-PLAN.md §2.3
    /// for why the inert behaviour is reported rather than asserted.
    /// </para>
    /// </summary>
    [Fact]
    public void ACommandUsingTheOptionAttributesCompiles() {
        ConsoleGeneratorTest.Generate(ConsoleGeneratorTest.WithApplication(
            """
            [Command("import")]
            public class ImportCommand {
                [Option(Name = "source-file", Description = "Where to read from")]
                public string Source { get; set; } = "";

                [FileOption(Name = "config", Description = "Configuration file")]
                public string Config { get; set; } = "";

                [ExcludeOption]
                public string Internal { get; set; } = "";
            }

            public class ImportHandler : ICommandHandler<ImportCommand> {
                public Task<int> Handle(ImportCommand value) => Task.FromResult(0);
            }
            """)).AssertNoErrors();
    }

    /// <summary>
    /// A command class with fields and methods as well as properties. Only properties become
    /// options; anything else has to be stepped over rather than tripped over.
    /// </summary>
    [Fact]
    public void ACommandWithNonPropertyMembersCompiles() {
        ConsoleGeneratorTest.Generate(ConsoleGeneratorTest.WithApplication(
            """
            [Command("build")]
            public class BuildCommand {
                private readonly int _cached = 3;

                public string Target { get; set; } = "";

                public int Describe() => _cached;
            }

            public class BuildHandler : ICommandHandler<BuildCommand> {
                public Task<int> Handle(BuildCommand value) => Task.FromResult(0);
            }
            """)).AssertNoErrors();
    }

    /// <summary>
    /// A description carrying the punctuation help text actually contains. The generator lifts the
    /// attribute argument's raw source text and re-wraps it in quotes, so anything that has to
    /// survive that round trip belongs here.
    ///
    /// <para>
    /// A description containing an <em>escaped double quote</em> is deliberately absent: it emits
    /// C# that does not compile, because trimming quotes off the raw literal strips the escape's
    /// closing quote as well. Reported rather than asserted — see TESTING-PLAN.md §2.1 for the
    /// defect class and §6 conventions rule 7 for why the bug is not encoded here.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("Copy from C:\\\\src to C:\\\\dst")]
    [InlineData("Add, subtract, multiply")]
    [InlineData("Don't stop on the first error")]
    [InlineData("Filter with a pattern such as *.cs")]
    public void ADescriptionCarryingPunctuationCompiles(string description) {
        ConsoleGeneratorTest.Generate(ConsoleGeneratorTest.WithApplication(
            $$"""
            [Command("copy", Description = "{{description}}")]
            public class CopyCommand {
                public string Target { get; set; } = "";
            }

            public class CopyHandler : ICommandHandler<CopyCommand> {
                public Task<int> Handle(CopyCommand value) => Task.FromResult(0);
            }
            """)).AssertNoErrors();
    }

    /// <summary>
    /// A command whose name matches a C# keyword. Names reach the generated code as string literals
    /// only, so this must not turn into an identifier.
    /// </summary>
    [Fact]
    public void ACommandNamedForAKeywordCompiles() {
        ConsoleGeneratorTest.Generate(ConsoleGeneratorTest.WithApplication(
            """
            [Command("new")]
            public class NewCommand {
                public string Template { get; set; } = "";
            }

            public class NewHandler : ICommandHandler<NewCommand> {
                public Task<int> Handle(NewCommand value) => Task.FromResult(0);
            }
            """)).AssertNoErrors();
    }

    /// <summary>A source with no entry point at all produces nothing and reports nothing.</summary>
    [Fact]
    public void ASourceWithNoEntryPointGeneratesNothing() {
        var result = ConsoleGeneratorTest.GenerateLibrary(
            """
            namespace TestApp;

            public class NotAnEntryPoint {
                public string Value => "x";
            }
            """);

        Assert.Empty(result.GeneratedSources);
    }
}
