using Hardened.Commands.Impl;
using Xunit;

namespace Hardened.Commands.Tests.Impl;

/// <summary>
/// Command resolution: how the parser gets from the leading arguments to a node in the command
/// tree, and what it reports when it cannot.
/// </summary>
public class CommandLineParserCommandTests {

    [Fact]
    public async Task AnUnknownCommandIsReportedAsNotFound() {
        var parser = CommandLineTest.Parser(CommandLineTest.Command("greet"));

        var result = await parser.ParseCommandLineArguments(["bogus"]);

        Assert.Equal(ParseResultStatus.NoCommandFound, result.ResultStatus);
        Assert.Null(result.CommandTreeNode);
    }

    /// <summary>
    /// An unknown subcommand keeps the parent it was looked for under. The printer uses that to say
    /// "<c>x</c> is not a valid subcommand of <c>y</c>" and to list <c>y</c>'s subcommands — with a
    /// null node it can only print the top-level command list.
    /// </summary>
    [Fact]
    public async Task AnUnknownSubcommandKeepsTheParentItWasLookedForUnder() {
        var parser = CommandLineTest.Parser(
            CommandLineTest.Command("math"),
            CommandLineTest.SubCommand("math", "add"));

        var result = await parser.ParseCommandLineArguments(["math", "bogus"]);

        Assert.Equal(ParseResultStatus.NoCommandFound, result.ResultStatus);
        Assert.Equal("math", result.CommandTreeNode?.Command.CommandName);
    }

    /// <summary>
    /// A command with subcommands is not runnable on its own, and saying so is different from
    /// saying the command does not exist.
    /// </summary>
    [Fact]
    public async Task AParentCommandGivenAloneAsksForASubcommand() {
        var parser = CommandLineTest.Parser(
            CommandLineTest.Command("math"),
            CommandLineTest.SubCommand("math", "add"));

        var result = await parser.ParseCommandLineArguments(["math"]);

        Assert.Equal(ParseResultStatus.NoSubCommandProvided, result.ResultStatus);
        Assert.Equal("math", result.CommandTreeNode?.Command.CommandName);
    }

    [Theory]
    [InlineData("math", "add")]
    [InlineData("Math", "Add")]
    [InlineData("MATH", "ADD")]
    public async Task CommandNamesMatchWithoutRegardToCase(string parent, string child) {
        var parser = CommandLineTest.Parser(
            CommandLineTest.Command("math", options: CommandLineTest.Required("x")),
            CommandLineTest.SubCommand("math", "add"));

        var result = await parser.ParseCommandLineArguments([parent, child, "--x", "1"]);

        Assert.Equal(ParseResultStatus.Success, result.ResultStatus);
        Assert.Equal("add", result.CommandTreeNode?.Command.CommandName);
    }

    /// <summary>
    /// The inheritance the console generator relies on: a subcommand declares no options of its
    /// own and picks up its parent's. This is exactly the shape the generator emits for
    /// <c>class AddCommand : MathCommand</c> — <c>add</c>'s option array is empty and <c>math</c>
    /// carries <c>x</c> and <c>y</c>.
    /// </summary>
    [Fact]
    public async Task ASubcommandAcceptsItsParentsOptions() {
        var parser = CommandLineTest.Parser(
            CommandLineTest.Command("math",
                options: [CommandLineTest.Required("x"), CommandLineTest.Required("y")]),
            CommandLineTest.SubCommand("math", "add"));

        var result = await parser.ParseCommandLineArguments(["math", "add", "--x", "1", "--y", "2"]);

        Assert.Equal(ParseResultStatus.Success, result.ResultStatus);
        Assert.Equal("1", Assert.Single(result.Options["x"]));
        Assert.Equal("2", Assert.Single(result.Options["y"]));
    }

    /// <summary>
    /// Leaving out an option the subcommand inherited is a missing option, named for the parent's
    /// declaration rather than silently defaulted.
    /// </summary>
    [Fact]
    public async Task AnInheritedRequiredOptionIsStillRequired() {
        var parser = CommandLineTest.Parser(
            CommandLineTest.Command("math",
                options: [CommandLineTest.Required("x"), CommandLineTest.Required("y")]),
            CommandLineTest.SubCommand("math", "add"));

        var result = await parser.ParseCommandLineArguments(["math", "add", "--x", "1"]);

        Assert.Equal(ParseResultStatus.MissingOption, result.ResultStatus);
        Assert.Equal("y", Assert.Single(result.MissingOptions!).OptionName);
    }

    /// <summary>A subcommand's own options and its parent's are both accepted together.</summary>
    [Fact]
    public async Task ASubcommandCombinesItsOwnOptionsWithItsParents() {
        var parser = CommandLineTest.Parser(
            CommandLineTest.Command("db", options: CommandLineTest.Required("connection")),
            CommandLineTest.SubCommand("db", "migrate", options: CommandLineTest.Required("target")));

        var result = await parser.ParseCommandLineArguments(
            ["db", "migrate", "--connection", "local", "--target", "20260811"]);

        Assert.Equal(ParseResultStatus.Success, result.ResultStatus);
        Assert.Equal("local", Assert.Single(result.Options["connection"]));
        Assert.Equal("20260811", Assert.Single(result.Options["target"]));
    }

    /// <summary>
    /// With no root command declared, an application invoked with no arguments has nothing to run,
    /// so it prints help rather than failing.
    /// </summary>
    [Fact]
    public async Task NoArgumentsWithNoRootCommandIsHelp() {
        var parser = CommandLineTest.Parser(CommandLineTest.Command("greet"));

        var result = await parser.ParseCommandLineArguments([]);

        Assert.Equal(ParseResultStatus.Help, result.ResultStatus);
    }

    /// <summary>
    /// An application may declare one unnamed command, which runs when no command is given. That is
    /// how a single-purpose tool is written.
    /// </summary>
    [Fact]
    public async Task ARootCommandRunsWithNoCommandNameOnTheCommandLine() {
        var parser = CommandLineTest.Parser(
            CommandLineTest.RootCommand(CommandLineTest.Required("name")));

        var result = await parser.ParseCommandLineArguments(["--name", "Ada"]);

        Assert.Equal(ParseResultStatus.Success, result.ResultStatus);
        Assert.Equal("", result.CommandTreeNode?.Command.CommandName);
        Assert.Equal("Ada", Assert.Single(result.Options["name"]));
    }

    /// <summary>A root command that needs no options runs on a bare invocation.</summary>
    [Fact]
    public async Task ARootCommandWithNoOptionsRunsOnNoArgumentsAtAll() {
        var parser = CommandLineTest.Parser(CommandLineTest.RootCommand());

        var result = await parser.ParseCommandLineArguments([]);

        Assert.Equal(ParseResultStatus.Success, result.ResultStatus);
    }

    /// <summary>
    /// <c>--help</c> before any command reaches the root and is reported as help. This is the top of
    /// the three levels: root here, leaf command in
    /// <see cref="CommandLineParserOptionTests.HelpOnACommandIsRecognisedRatherThanRejected"/>,
    /// subcommand in <see cref="CommandLineParserOptionTests.HelpOnASubcommandKeepsThatSubcommand"/>.
    /// </summary>
    [Fact]
    public async Task HelpBeforeAnyCommandIsRecognised() {
        var parser = CommandLineTest.Parser(
            CommandLineTest.RootCommand(CommandLineTest.Required("name")));

        var result = await parser.ParseCommandLineArguments(["--help"]);

        Assert.Equal(ParseResultStatus.Help, result.ResultStatus);
    }

    /// <summary>
    /// An option where a command was expected, with no root command to take it, is not an unknown
    /// option — there is nothing to match it against yet.
    /// </summary>
    [Fact]
    public async Task AnOptionWithNoRootCommandToTakeItIsHelp() {
        var parser = CommandLineTest.Parser(CommandLineTest.Command("greet"));

        var result = await parser.ParseCommandLineArguments(["--name", "Ada"]);

        Assert.Equal(ParseResultStatus.Help, result.ResultStatus);
    }

    /// <summary>An application that declares nothing at all still answers, rather than throwing.</summary>
    [Fact]
    public async Task AnApplicationWithNoCommandsAtAllIsHelp() {
        var parser = CommandLineTest.Parser();

        var result = await parser.ParseCommandLineArguments([]);

        Assert.Equal(ParseResultStatus.Help, result.ResultStatus);
    }

    /// <summary>Three sibling commands under one parent all resolve to themselves.</summary>
    [Theory]
    [InlineData("add")]
    [InlineData("subtract")]
    [InlineData("multiply")]
    public async Task EachSubcommandResolvesToItself(string subcommand) {
        var parser = CommandLineTest.Parser(
            CommandLineTest.Command("math", options: CommandLineTest.Required("x")),
            CommandLineTest.SubCommand("math", "add"),
            CommandLineTest.SubCommand("math", "subtract"),
            CommandLineTest.SubCommand("math", "multiply"));

        var result = await parser.ParseCommandLineArguments(["math", subcommand, "--x", "1"]);

        Assert.Equal(ParseResultStatus.Success, result.ResultStatus);
        Assert.Equal(subcommand, result.CommandTreeNode?.Command.CommandName);
    }
}
