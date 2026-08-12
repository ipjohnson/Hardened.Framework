using Hardened.Commands.Impl;
using Xunit;

namespace Hardened.Commands.Tests.Impl;

/// <summary>
/// Option parsing: what the parser makes of everything after the command name.
///
/// <para>
/// The three existing parser suites cover one shape each — root, single command, nested — and only
/// the happy path plus a missing required option. Everything below is a branch none of them takes.
/// </para>
/// </summary>
public class CommandLineParserOptionTests {

    [Fact]
    public async Task AnOptionTakesItsValueFromTheFollowingArgument() {
        var parser = CommandLineTest.Parser(
            CommandLineTest.Command("greet", options: CommandLineTest.Required("name")));

        var result = await parser.ParseCommandLineArguments(["greet", "--name", "Ada"]);

        Assert.Equal(ParseResultStatus.Success, result.ResultStatus);
        Assert.Equal("Ada", Assert.Single(result.Options["name"]));
    }

    /// <summary>
    /// Quoting is the shell's job — by the time the parser sees the arguments the quotes are gone
    /// and the value is one element. What the parser must not do is split it again.
    /// </summary>
    [Fact]
    public async Task AnOptionValueContainingSpacesArrivesAsOneValue() {
        var parser = CommandLineTest.Parser(
            CommandLineTest.Command("say", options: CommandLineTest.Required("message")));

        var result = await parser.ParseCommandLineArguments(["say", "--message", "hello there world"]);

        Assert.Equal(ParseResultStatus.Success, result.ResultStatus);
        Assert.Equal("hello there world", Assert.Single(result.Options["message"]));
    }

    /// <summary>
    /// A boolean option is written the same way as any other: name then value. There is no bare
    /// <c>--flag</c> form — the parser always consumes the next argument as the value.
    /// </summary>
    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public async Task ABooleanOptionCarriesItsValueLikeAnyOther(string value) {
        var parser = CommandLineTest.Parser(
            CommandLineTest.Command("build",
                options: CommandLineTest.Required("verbose", CommandOptionType.Boolean)));

        var result = await parser.ParseCommandLineArguments(["build", "--verbose", value]);

        Assert.Equal(ParseResultStatus.Success, result.ResultStatus);
        Assert.Equal(value, Assert.Single(result.Options["verbose"]));
    }

    /// <summary>
    /// The parser lowercases the argument before matching, and the generator emits lowercased
    /// option names, so the option is reachable however the user cased it.
    /// </summary>
    [Theory]
    [InlineData("--name")]
    [InlineData("--Name")]
    [InlineData("--NAME")]
    public async Task OptionNamesMatchWithoutRegardToCase(string written) {
        var parser = CommandLineTest.Parser(
            CommandLineTest.Command("greet", options: CommandLineTest.Required("name")));

        var result = await parser.ParseCommandLineArguments(["greet", written, "Ada"]);

        Assert.Equal(ParseResultStatus.Success, result.ResultStatus);
        Assert.Equal("Ada", Assert.Single(result.Options["name"]));
    }

    /// <summary>
    /// An unknown option is named in the result. The name is what the printer puts in front of the
    /// user, so getting the status right while losing the name produces "Invalid option: " and a
    /// help screen the user has to read to work out which one.
    /// </summary>
    [Fact]
    public async Task AnUnknownOptionIsReportedByName() {
        var parser = CommandLineTest.Parser(
            CommandLineTest.Command("greet", options: CommandLineTest.Required("name")));

        var result = await parser.ParseCommandLineArguments(["greet", "--nickname", "Ada"]);

        Assert.Equal(ParseResultStatus.InvalidOption, result.ResultStatus);
        Assert.Equal("--nickname", result.InvalidOption);
    }

    /// <summary>A stray value where an option was expected is rejected rather than ignored.</summary>
    [Fact]
    public async Task AnArgumentWithoutTheOptionPrefixIsRejected() {
        var parser = CommandLineTest.Parser(
            CommandLineTest.Command("greet", options: CommandLineTest.Required("name")));

        var result = await parser.ParseCommandLineArguments(["greet", "--name", "Ada", "stray"]);

        Assert.Equal(ParseResultStatus.InvalidOption, result.ResultStatus);
    }

    [Fact]
    public async Task EveryMissingRequiredOptionIsNamed() {
        var parser = CommandLineTest.Parser(
            CommandLineTest.Command("send",
                options: [
                    CommandLineTest.Required("to"),
                    CommandLineTest.Required("subject"),
                    CommandLineTest.Required("body")
                ]));

        var result = await parser.ParseCommandLineArguments(["send", "--to", "ada@example.com"]);

        Assert.Equal(ParseResultStatus.MissingOption, result.ResultStatus);
        Assert.Equal(
            ["subject", "body"],
            result.MissingOptions?.Select(option => option.OptionName));
    }

    /// <summary>An option that is not required may simply be left off.</summary>
    [Fact]
    public async Task AnOptionalOptionMayBeOmitted() {
        var parser = CommandLineTest.Parser(
            CommandLineTest.Command("greet",
                options: [CommandLineTest.Required("name"), CommandLineTest.Optional("greeting")]));

        var result = await parser.ParseCommandLineArguments(["greet", "--name", "Ada"]);

        Assert.Equal(ParseResultStatus.Success, result.ResultStatus);
        Assert.False(result.Options.ContainsKey("greeting"));
    }

    [Fact]
    public async Task AnOptionalOptionIsBoundWhenItIsGiven() {
        var parser = CommandLineTest.Parser(
            CommandLineTest.Command("greet",
                options: [CommandLineTest.Required("name"), CommandLineTest.Optional("greeting")]));

        var result = await parser.ParseCommandLineArguments(
            ["greet", "--name", "Ada", "--greeting", "Hi"]);

        Assert.Equal(ParseResultStatus.Success, result.ResultStatus);
        Assert.Equal("Hi", Assert.Single(result.Options["greeting"]));
    }

    /// <summary>
    /// An array option keeps taking values until something that looks like another option, so
    /// <c>--tags a b c</c> is three values rather than one value and two stray arguments.
    /// </summary>
    [Fact]
    public async Task AnArrayOptionCollectsValuesUntilTheNextOption() {
        var parser = CommandLineTest.Parser(
            CommandLineTest.Command("tag",
                options: [CommandLineTest.Array("tags"), CommandLineTest.Required("name")]));

        var result = await parser.ParseCommandLineArguments(
            ["tag", "--tags", "red", "green", "blue", "--name", "Ada"]);

        Assert.Equal(ParseResultStatus.Success, result.ResultStatus);
        Assert.Equal(["red", "green", "blue"], result.Options["tags"]);
        Assert.Equal("Ada", Assert.Single(result.Options["name"]));
    }

    [Fact]
    public async Task AnArrayOptionTakesEveryRemainingValueWhenItComesLast() {
        var parser = CommandLineTest.Parser(
            CommandLineTest.Command("tag",
                options: [CommandLineTest.Required("name"), CommandLineTest.Array("tags")]));

        var result = await parser.ParseCommandLineArguments(
            ["tag", "--name", "Ada", "--tags", "red", "green"]);

        Assert.Equal(ParseResultStatus.Success, result.ResultStatus);
        Assert.Equal(["red", "green"], result.Options["tags"]);
    }

    /// <summary>
    /// The prefix comes from <c>CommandLineParserOptions</c>, so an application can use <c>/name</c>
    /// or <c>-name</c> instead. Both the option match and the "is this a command" test read it.
    /// </summary>
    [Theory]
    [InlineData("-")]
    [InlineData("/")]
    [InlineData("--")]
    public async Task TheOptionPrefixIsConfigurable(string prefix) {
        var parser = CommandLineTest.Parser(prefix,
            CommandLineTest.Command("greet", options: CommandLineTest.Required("name")));

        var result = await parser.ParseCommandLineArguments(["greet", prefix + "name", "Ada"]);

        Assert.Equal(ParseResultStatus.Success, result.ResultStatus);
        Assert.Equal("Ada", Assert.Single(result.Options["name"]));
    }

    /// <summary>
    /// <c>--help</c> on a leaf command is help, not an unknown option, and the result keeps the
    /// command it was asked about so the printer can list that command's options.
    /// </summary>
    [Fact]
    public async Task HelpOnACommandIsRecognisedRatherThanRejected() {
        var parser = CommandLineTest.Parser(
            CommandLineTest.Command("greet", options: CommandLineTest.Required("name")));

        var result = await parser.ParseCommandLineArguments(["greet", "--help"]);

        Assert.Equal(ParseResultStatus.Help, result.ResultStatus);
        Assert.Equal("greet", result.CommandTreeNode?.Command.CommandName);
    }

    /// <summary>
    /// <c>--help</c> on a subcommand reaches that subcommand. The help printer walks up from here
    /// to list inherited options, so losing the node loses the parent's options too.
    /// </summary>
    [Fact]
    public async Task HelpOnASubcommandKeepsThatSubcommand() {
        var parser = CommandLineTest.Parser(
            CommandLineTest.Command("math", options: CommandLineTest.Required("x")),
            CommandLineTest.SubCommand("math", "add"));

        var result = await parser.ParseCommandLineArguments(["math", "add", "--help"]);

        Assert.Equal(ParseResultStatus.Help, result.ResultStatus);
        Assert.Equal("add", result.CommandTreeNode?.Command.CommandName);
    }

    /// <summary>
    /// <c>--help</c> asked for after some options still reports help rather than the missing ones —
    /// asking how to use a command should not require using it correctly first.
    /// </summary>
    [Fact]
    public async Task HelpWinsOverAMissingRequiredOption() {
        var parser = CommandLineTest.Parser(
            CommandLineTest.Command("send",
                options: [CommandLineTest.Required("to"), CommandLineTest.Required("body")]));

        var result = await parser.ParseCommandLineArguments(
            ["send", "--to", "ada@example.com", "--help"]);

        Assert.Equal(ParseResultStatus.Help, result.ResultStatus);
    }

    /// <summary>
    /// A command line with no options at all against a command that requires none. The result has
    /// to be success rather than the empty-collection edge case reporting something missing.
    /// </summary>
    [Fact]
    public async Task ACommandThatNeedsNoOptionsSucceedsWithNone() {
        var parser = CommandLineTest.Parser(CommandLineTest.Command("version"));

        var result = await parser.ParseCommandLineArguments(["version"]);

        Assert.Equal(ParseResultStatus.Success, result.ResultStatus);
        Assert.Empty(result.Options);
    }
}
