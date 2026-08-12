using Xunit;

namespace Hardened.Console.SourceGenerator.Tests;

/// <summary>
/// What the console generator writes, as opposed to whether it compiles — that is
/// <see cref="GeneratedCodeCompilesTests"/>'s job. Every case here compiles its output as well,
/// because <see cref="ConsoleGeneratorTest.EmittedEntryPoint"/> runs
/// <see cref="SourceGeneration.Testing.GeneratorResult.AssertNoErrors"/> first.
///
/// <para>
/// These assertions exist because the emitted <c>CommandDefinitionProvider</c> is the only thing
/// connecting a <c>[Command]</c> attribute to the parser's command tree. A definition emitted with
/// the wrong parent name, or an option list that silently drops a property, compiles perfectly and
/// produces an application whose subcommand cannot be reached.
/// </para>
/// </summary>
public class CommandDefinitionEmitTests {

    private const string GreetCommand =
        """
        [Command("greet", Description = "Say hello")]
        public class GreetCommand {
            public string Name { get; set; } = "";
        }
        """;

    private const string MathAndAdd =
        """
        [Command("math", Description = "Math commands")]
        public class MathCommand {
            public int X { get; set; }
            public int Y { get; set; }
        }

        [Command("add", ParentCommand = "math", Description = "Add two numbers")]
        public class AddCommand : MathCommand { }
        """;

    /// <summary>The generated file is named for the entry point type, not for the commands.</summary>
    [Fact]
    public void TheGeneratedFileIsNamedForTheEntryPoint() {
        var result = ConsoleGeneratorTest
            .Generate(ConsoleGeneratorTest.WithApplication(GreetCommand))
            .AssertNoErrors();

        Assert.Equal("Application.Commands.cs", Assert.Single(result.GeneratedSources).Key);
    }

    /// <summary>
    /// A top-level command carries an empty parent name, which is what
    /// <c>CommandLineDefinitionService</c> tests with <c>string.IsNullOrEmpty</c> to decide a
    /// command hangs off the root.
    /// </summary>
    [Fact]
    public void ATopLevelCommandIsEmittedWithNoParentName() {
        var source = ConsoleGeneratorTest.EmittedEntryPoint(
            ConsoleGeneratorTest.WithApplication(GreetCommand));

        Assert.Contains(
            """
            new global::Hardened.Commands.Impl.CommandDefinition( "", "greet",
            """,
            source);
    }

    /// <summary>
    /// A subcommand's <c>ParentCommand</c> reaches the emitted definition. Without it the command
    /// is registered at the root and <c>math add</c> never resolves.
    /// </summary>
    [Fact]
    public void ASubcommandIsEmittedWithItsParentCommandName() {
        var source = ConsoleGeneratorTest.EmittedEntryPoint(
            ConsoleGeneratorTest.WithApplication(MathAndAdd));

        Assert.Contains(
            """
            new global::Hardened.Commands.Impl.CommandDefinition( "math", "add",
            """,
            source);
    }

    /// <summary>The description on the attribute is what <c>--help</c> prints.</summary>
    [Fact]
    public void TheDescriptionReachesTheEmittedDefinition() {
        var source = ConsoleGeneratorTest.EmittedEntryPoint(
            ConsoleGeneratorTest.WithApplication(GreetCommand));

        Assert.Contains(""""
            "", "greet", "", "Say hello",
            """", source);
    }

    /// <summary>
    /// One <c>CommandOption</c> per declared property, named for the lowercased property. The
    /// parser lowercases the argument before matching, so a mixed-case option name here would make
    /// the option unreachable from the command line.
    /// </summary>
    [Fact]
    public void EachPropertyBecomesAnOptionNamedForTheLowercasedProperty() {
        var source = ConsoleGeneratorTest.EmittedEntryPoint(ConsoleGeneratorTest.WithApplication(
            """
            [Command("send")]
            public class SendCommand {
                public string Recipient { get; set; } = "";
                public string MessageBody { get; set; } = "";
            }
            """));

        Assert.Contains(
            """
            new global::Hardened.Commands.Impl.CommandOption( "recipient",
            """,
            source);
        Assert.Contains(
            """
            new global::Hardened.Commands.Impl.CommandOption( "messagebody",
            """,
            source);
    }

    /// <summary>
    /// A command with no properties of its own emits an empty option array rather than omitting the
    /// argument — <c>CommandDefinition.Options</c> is not nullable.
    /// </summary>
    [Fact]
    public void ACommandWithNoPropertiesEmitsAnEmptyOptionArray() {
        var source = ConsoleGeneratorTest.EmittedEntryPoint(ConsoleGeneratorTest.WithApplication(
            """
            [Command("version")]
            public class VersionCommand { }
            """));

        Assert.Contains("new CommandOption[]{ },", source);
    }

    /// <summary>
    /// A subcommand does not re-declare its parent's options. It gets them at run time from
    /// <c>CommandLineParser.GetAllOptions</c>, which walks up the tree, and at bind time from the
    /// parent binder the generated binder resolves. Emitting them twice would double every option
    /// in the help output.
    /// </summary>
    [Fact]
    public void ASubcommandDoesNotRepeatItsParentsOptions() {
        var source = ConsoleGeneratorTest.EmittedEntryPoint(
            ConsoleGeneratorTest.WithApplication(MathAndAdd));

        Assert.Contains(
            """
            "math", "add", "", "Add two numbers", new CommandOption[]{ },
            """,
            source);
        Assert.Contains(
            """
            new global::Hardened.Commands.Impl.CommandOption( "x",
            """,
            source);
        Assert.Contains(
            """
            new global::Hardened.Commands.Impl.CommandOption( "y",
            """,
            source);
    }

    /// <summary>
    /// The subcommand's binder delegates to the parent's rather than binding the inherited
    /// properties itself. This is the whole mechanism behind inherited options: without it,
    /// <c>math add --x 1</c> parses but leaves <c>X</c> at zero.
    /// </summary>
    [Fact]
    public void ASubcommandsBinderDelegatesToItsParentsBinder() {
        var source = ConsoleGeneratorTest.EmittedEntryPoint(
            ConsoleGeneratorTest.WithApplication(MathAndAdd));

        Assert.Contains(
            "_serviceProvider.GetService<global::Hardened.Commands.Impl.ICommandBinder<global::TestApp.MathCommand>>()",
            source);
    }

    /// <summary>
    /// Binder classes are nested in the entry point, so two commands with the same type name under
    /// different parents would collide. The generator prefixes the parent type's name to avoid it.
    /// </summary>
    [Fact]
    public void ASubcommandsBinderIsNamedForItsParentTypeAndItsOwn() {
        var source = ConsoleGeneratorTest.EmittedEntryPoint(
            ConsoleGeneratorTest.WithApplication(MathAndAdd));

        Assert.Contains("class MathCommand_AddCommandBinder", source);
        Assert.Contains("class MathCommandBinder", source);
    }

    /// <summary>
    /// Every command gets its binder registered, and the definition provider registered once. A
    /// missing binder registration is not a compile error — <c>CommandLineHelper</c> resolves it
    /// with <c>GetRequiredService</c> and throws at run time, on the command's first use.
    /// </summary>
    [Fact]
    public void EveryCommandsBinderIsRegisteredAndTheProviderRegisteredOnce() {
        var source = ConsoleGeneratorTest.EmittedEntryPoint(
            ConsoleGeneratorTest.WithApplication(MathAndAdd));

        Assert.Contains("typeof(global::Hardened.Commands.Impl.ICommandBinder<global::TestApp.MathCommand>)", source);
        Assert.Contains("typeof(global::Hardened.Commands.Impl.ICommandBinder<global::TestApp.AddCommand>)", source);
        Assert.Equal(
            1,
            source.Split("typeof(global::Hardened.Commands.Impl.ICommandDefinitionProvider)").Length - 1);
    }

    /// <summary>
    /// The command's run delegate is <c>CommandLineHelper&lt;T&gt;.Invoke</c>, which is what
    /// resolves the handler and returns its exit code. A definition emitted with a null delegate
    /// makes the command parse and then print help instead of running.
    /// </summary>
    [Fact]
    public void EachDefinitionRunsThroughCommandLineHelper() {
        var source = ConsoleGeneratorTest.EmittedEntryPoint(
            ConsoleGeneratorTest.WithApplication(GreetCommand));

        Assert.Contains("CommandLineHelper<GreetCommand>.Invoke", source);
    }

    /// <summary>
    /// The entry point gets a parameterless constructor, an <c>args</c> constructor and an
    /// environment constructor. <c>Program.cs</c> uses the second; the third is the only seam a
    /// test has for supplying arguments without touching the process command line.
    /// </summary>
    [Fact]
    public void TheEntryPointGetsAllThreeConstructors() {
        var source = ConsoleGeneratorTest.EmittedEntryPoint(
            ConsoleGeneratorTest.WithApplication(GreetCommand));

        Assert.Contains("public Application() : this(", source);
        Assert.Contains("public Application(string[] arguments) : this(", source);
        Assert.Contains(
            "public Application(global::Hardened.Shared.Runtime.Application.IHardenedEnvironment environment)",
            source);
    }

    /// <summary>
    /// <c>Run()</c> hands off to <c>ApplicationLogic.RunApplication</c>, which is what runs the
    /// startup services before the command and returns the command's exit code to the process.
    /// </summary>
    [Fact]
    public void RunDelegatesToApplicationLogic() {
        var source = ConsoleGeneratorTest.EmittedEntryPoint(
            ConsoleGeneratorTest.WithApplication(GreetCommand));

        Assert.Contains(
            "global::Hardened.Shared.Runtime.Application.ApplicationLogic.RunApplication(",
            source);
    }

    /// <summary>
    /// Command registration is hung off <c>DependencyRegistry&lt;T&gt;</c> from a static field, and
    /// the field carries <c>[DynamicDependency]</c> so the trimmer keeps the method. Losing that
    /// attribute produces an application that works in a normal build and has no commands after
    /// publishing trimmed — which matters for an AOT-focused framework.
    /// </summary>
    [Fact]
    public void CommandRegistrationIsRootedForTheTrimmer() {
        var source = ConsoleGeneratorTest.EmittedEntryPoint(
            ConsoleGeneratorTest.WithApplication(GreetCommand));

        Assert.Contains("[DynamicDependency(nameof(RegisterCommands))]", source);
        Assert.Contains("DependencyRegistry<Application>.Add(RegisterCommands)", source);
    }

    /// <summary>
    /// Every option the generator emits is required, typed <c>String</c>, not an array, and carries
    /// an empty description.
    ///
    /// <para>
    /// That is not a design surfaced through the attributes — <c>GetDefaultAndRequired</c>,
    /// <c>GetOptionTypeAndArray</c> and <c>GetNameAndDescription</c> return constants and never
    /// inspect the property or its attributes. It is asserted here because it is the behaviour
    /// every consumer gets today, and a change to it would otherwise be invisible until an
    /// application started rejecting command lines that used to work. See the workstream report for
    /// why <c>[Option]</c>, <c>[FileOption]</c> and <c>[ExcludeOption]</c> cannot influence it.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryEmittedOptionIsRequiredAndTypedAsAString() {
        var source = ConsoleGeneratorTest.EmittedEntryPoint(ConsoleGeneratorTest.WithApplication(
            """
            [Command("greet")]
            public class GreetCommand {
                public string Name { get; set; } = "";
                public bool Loud { get; set; }
            }
            """));

        Assert.Contains(
            """
            new global::Hardened.Commands.Impl.CommandOption( "name", CommandOptionType.String, "", true, false )
            """,
            source);
        Assert.Contains(
            """
            new global::Hardened.Commands.Impl.CommandOption( "loud", CommandOptionType.String, "", true, false )
            """,
            source);
    }
}
