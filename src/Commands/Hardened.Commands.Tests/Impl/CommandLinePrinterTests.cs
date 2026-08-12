using Hardened.Commands.Impl;
using Hardened.Shared.Runtime.Application;
using NSubstitute;
using Xunit;

namespace Hardened.Commands.Tests.Impl;

/// <summary>
/// What the user reads when a command line does not run: help, missing options, unknown options and
/// unknown commands.
///
/// <para>
/// Output is captured through <c>IConsoleOutputService</c>, the seam the framework writes its help
/// through, rather than by redirecting <c>Console.Out</c>. Redirecting the process's console is
/// global state, and these suites run alongside others in the same process.
/// </para>
///
/// <para>
/// That seam is not complete: <c>CommandLinePrinter</c> writes the blank lines around its output,
/// and the "command not found" and "missing subcommand" messages themselves, straight to
/// <c>Console.WriteLine</c>. Those lines cannot be asserted on without redirecting global state, so
/// the tests below assert the parts that do go through the seam and the gap is reported rather than
/// worked around.
/// </para>
/// </summary>
public class CommandLinePrinterTests {

    private static (CommandLinePrinter Printer, List<string> Written) Printer(
        params CommandDefinition[] definitions) {
        var written = new List<string>();
        var output = Substitute.For<IConsoleOutputService>();
        output.WhenForAnyArgs(service => service.WriteLine(default!))
            .Do(call => written.Add(call.Arg<string>()));

        var printer = new CommandLinePrinter(
            CommandLineTest.DefinitionService(definitions),
            new EnvironmentImpl(),
            output,
            CommandLineTest.ParserOptions());

        return (printer, written);
    }

    private static ParseResult Result(
        ParseResultStatus status,
        CommandTreeNode? node = null,
        IReadOnlyList<CommandOption>? missing = null,
        string? invalidOption = null) =>
        new(status, node, new Dictionary<string, string[]>(), "", missing, invalidOption);

    private static CommandTreeNode Node(string name, params CommandOption[] options) =>
        new(CommandLineTest.Command(name, "", options), []);

    /// <summary>
    /// Help for an application with commands lists them with their descriptions — that list is the
    /// whole point of asking.
    /// </summary>
    [Fact]
    public async Task HelpListsEveryCommandWithItsDescription() {
        var (printer, written) = Printer(
            CommandLineTest.Command("greet", "Say hello"),
            CommandLineTest.Command("version", "Print the version"));

        await printer.PrintParseResult(Result(ParseResultStatus.Help));

        Assert.Contains(written, line => line.Contains("greet") && line.Contains("Say hello"));
        Assert.Contains(written, line => line.Contains("version") && line.Contains("Print the version"));
    }

    /// <summary>Help always says how the command is invoked before saying what there is.</summary>
    [Fact]
    public async Task HelpLeadsWithAUsageLine() {
        var (printer, written) = Printer(CommandLineTest.Command("greet", "Say hello"));

        await printer.PrintParseResult(Result(ParseResultStatus.Help));

        Assert.Contains("Usage:", written);
        Assert.Contains(written, line => line.Contains("<command>"));
    }

    /// <summary>
    /// With subcommands in the tree the usage line says so, because <c>app &lt;command&gt;</c> alone
    /// would be wrong for every one of them.
    /// </summary>
    [Fact]
    public async Task TheUsageLineMentionsSubcommandsWhenThereAreAny() {
        var (printer, written) = Printer(
            CommandLineTest.Command("math", "Math commands"),
            CommandLineTest.SubCommand("math", "add", "Add two numbers"));

        await printer.PrintParseResult(Result(ParseResultStatus.Help));

        Assert.Contains(written, line => line.Contains("<command> <subcommand>"));
    }

    // There is deliberately no test here for "--help on an application with only a root command
    // lists its options". That path does not work, and asserting what it currently does would be
    // asserting the defect.
    //
    // CommandLineDefinitionService.GetRootCommand picks the empty-named command out of the
    // definitions, but GenerateTree still puts every definition whose ParentCommandName is empty
    // into the top-level dictionary - including that same root command. The root therefore becomes
    // its own child, CommandLinePrinter.PrintHelp sees ChildCommands.Count > 0, and a single-command
    // application prints "Usage: app  <subcommand> [options]" followed by an empty "Commands:"
    // header instead of its own options.
    //
    // WriteCommandOptionsHelp is consequently near-unreachable from Help: it needs zero child
    // commands, which needs zero definitions, and it then throws on options.Select(...).Max() over
    // an empty sequence. It is reached in practice only through the MissingOption path below.
    //
    // Found 2026-08-11 while covering this printer. Reported, not fixed - the fix changes
    // user-visible help output. See TESTING-PLAN.md.

    /// <summary>
    /// Required and optional options are labelled, so a user reading help knows which ones they can
    /// leave off without running the command to find out.
    ///
    /// <para>
    /// Driven through the missing-option path because that is the reachable route to
    /// <c>WriteCommandOptionsHelp</c> — see the note above.
    /// </para>
    /// </summary>
    [Fact]
    public async Task OptionsAreLabelledRequiredOrOptional() {
        var (printer, written) = Printer(CommandLineTest.Command("greet", "Say hello"));

        await printer.PrintParseResult(Result(
            ParseResultStatus.MissingOption,
            Node("greet",
                CommandLineTest.Required("name", description: "Who to greet"),
                CommandLineTest.Optional("greeting", description: "What to say")),
            missing: [CommandLineTest.Required("name", description: "Who to greet")]));

        Assert.Contains("Options:", written);
        Assert.Contains(written, line => line.Contains("--name") && line.Contains("[required]"));
        Assert.Contains(written, line => line.Contains("--greeting") && line.Contains("[optional]"));
    }

    /// <summary>Every missing option is named, and named the way it is written on the command line.</summary>
    [Fact]
    public async Task MissingOptionsAreNamedWithTheirPrefix() {
        var (printer, written) = Printer(CommandLineTest.Command("send", "Send a message"));

        await printer.PrintParseResult(Result(
            ParseResultStatus.MissingOption,
            Node("send", CommandLineTest.Required("to"), CommandLineTest.Required("subject")),
            missing: [CommandLineTest.Required("to"), CommandLineTest.Required("subject")]));

        var missingLine = Assert.Single(written, line => line.StartsWith("Missing options:"));

        Assert.Contains("--to", missingLine);
        Assert.Contains("--subject", missingLine);
    }

    /// <summary>
    /// The missing-option message is followed by the option list, so the user does not have to ask
    /// for help as a second step.
    /// </summary>
    [Fact]
    public async Task AMissingOptionIsFollowedByTheOptionList() {
        var (printer, written) = Printer(CommandLineTest.Command("send", "Send a message"));

        await printer.PrintParseResult(Result(
            ParseResultStatus.MissingOption,
            Node("send", CommandLineTest.Required("to", description: "Recipient")),
            missing: [CommandLineTest.Required("to", description: "Recipient")]));

        Assert.Contains("Options:", written);
        Assert.Contains(written, line => line.Contains("--to") && line.Contains("Recipient"));
    }

    /// <summary>An unknown option is quoted back so the user can see the typo.</summary>
    [Fact]
    public async Task AnUnknownOptionIsNamedInTheError() {
        var (printer, written) = Printer(CommandLineTest.Command("greet", "Say hello"));

        await printer.PrintParseResult(Result(
            ParseResultStatus.InvalidOption,
            Node("greet", CommandLineTest.Required("name")),
            invalidOption: "--nickname"));

        Assert.Contains("Invalid option: --nickname", written);
    }

    /// <summary>
    /// The usage line names the whole command path, not just the leaf, because <c>app add</c> is
    /// not what the user has to type for <c>app math add</c>.
    /// </summary>
    [Fact]
    public async Task TheUsageLineNamesTheFullCommandPath() {
        var (printer, written) = Printer(
            CommandLineTest.Command("math", "Math commands"),
            CommandLineTest.SubCommand("math", "add", "Add two numbers"));

        var math = new CommandTreeNode(CommandLineTest.Command("math", "Math commands"), []);
        var add = new CommandTreeNode(
            CommandLineTest.SubCommand("math", "add", "Add two numbers", CommandLineTest.Required("x")),
            []) { ParentCommand = math };

        await printer.PrintParseResult(Result(ParseResultStatus.InvalidOption, add, invalidOption: "--z"));

        Assert.Contains(written, line => line.Contains("math add [options]"));
    }

    /// <summary>
    /// Asking for a command's help lists that command's subcommands rather than the top-level ones.
    /// </summary>
    [Fact]
    public async Task NoSubcommandGivenListsThatCommandsSubcommands() {
        var (printer, written) = Printer(
            CommandLineTest.Command("math", "Math commands"),
            CommandLineTest.SubCommand("math", "add", "Add two numbers"),
            CommandLineTest.SubCommand("math", "subtract", "Subtract two numbers"),
            CommandLineTest.Command("greet", "Say hello"));

        await printer.PrintParseResult(
            Result(ParseResultStatus.NoSubCommandProvided, Node("math")));

        Assert.Contains(written, line => line.Contains("add") && line.Contains("Add two numbers"));
        Assert.Contains(written, line => line.Contains("subtract"));
        Assert.DoesNotContain(written, line => line.Contains("Say hello"));
    }

    /// <summary>
    /// An unknown command lists the commands that do exist, which is the only way the user finds
    /// the one they meant.
    /// </summary>
    [Fact]
    public async Task AnUnknownCommandIsFollowedByTheCommandList() {
        var (printer, written) = Printer(
            CommandLineTest.Command("greet", "Say hello"),
            CommandLineTest.Command("version", "Print the version"));

        await printer.PrintParseResult(Result(ParseResultStatus.NoCommandFound));

        Assert.Contains("Commands:", written);
        Assert.Contains(written, line => line.Contains("greet") && line.Contains("Say hello"));
    }

    /// <summary>
    /// A description too long for one line wraps rather than running off, and every word survives
    /// the wrap.
    /// </summary>
    [Fact]
    public async Task ALongDescriptionWrapsWithoutLosingWords() {
        var description =
            "Adds the two numbers given on the command line together and writes the sum to " +
            "standard output, which is rather more words than fit on a single terminal line";

        var (printer, written) = Printer(CommandLineTest.Command("add", description));

        await printer.PrintParseResult(Result(ParseResultStatus.Help));

        // Every line after the "Commands:" header belongs to the one command's description. Picking
        // lines by their content instead would silently drop the middle line of a three-line wrap,
        // and the "no words lost" assertion below is exactly the one that must not be fooled.
        var descriptionLines = written
            .Skip(written.IndexOf("Commands:") + 1)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        Assert.True(descriptionLines.Length > 1, "The long description did not wrap: " + string.Join("|", written));
        Assert.All(descriptionLines, line => Assert.True(
            line.Length < 120, $"A wrapped line ran to {line.Length} characters: {line}"));
        Assert.Equal(
            description.Split(' '),
            string.Join(' ', descriptionLines).Split(' ').Where(word => word.Length > 0).Skip(1));
    }

    /// <summary>
    /// A successful parse prints nothing — the command runs instead, and stray help output would
    /// land in the middle of the command's own.
    /// </summary>
    [Fact]
    public async Task ASuccessfulParsePrintsNothing() {
        var (printer, written) = Printer(CommandLineTest.Command("greet", "Say hello"));

        await printer.PrintParseResult(Result(ParseResultStatus.Success, Node("greet")));

        Assert.Empty(written);
    }
}
