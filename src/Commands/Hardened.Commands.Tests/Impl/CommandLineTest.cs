using Hardened.Commands.Impl;
using Microsoft.Extensions.Options;

namespace Hardened.Commands.Tests.Impl;

/// <summary>
/// Builds the command-line pieces by hand so a test can state its command tree inline.
///
/// <para>
/// The definition service is the real one rather than a substitute: tree building, parent linking
/// and the duplicate checks are part of what the parser depends on, and a test that mocked them
/// would pass over a tree no application could actually have.
/// </para>
/// </summary>
internal static class CommandLineTest {

    public static CommandLineParser Parser(params CommandDefinition[] definitions) =>
        Parser("--", definitions);

    public static CommandLineParser Parser(string optionPrefix, params CommandDefinition[] definitions) =>
        new(DefinitionService(definitions), ParserOptions(optionPrefix));

    public static CommandLineDefinitionService DefinitionService(params CommandDefinition[] definitions) =>
        new([new StubProvider(definitions)]);

    public static CommandLineDefinitionService DefinitionServiceOver(
        params ICommandDefinitionProvider[] providers) =>
        new(providers);

    public static IOptions<CommandLineParserOptions> ParserOptions(string optionPrefix = "--") =>
        Options.Create(new CommandLineParserOptions { OptionPrefix = optionPrefix });

    public static ICommandDefinitionProvider Provider(params CommandDefinition[] definitions) =>
        new StubProvider(definitions);

    /// <summary>A command with no parent, no subcommands and whatever options it is given.</summary>
    public static CommandDefinition Command(
        string name,
        string description = "",
        params CommandOption[] options) =>
        new(null, name, null, description, options, (_, _) => Task.FromResult(0));

    /// <summary>A command hanging off <paramref name="parent"/>.</summary>
    public static CommandDefinition SubCommand(
        string parent,
        string name,
        string description = "",
        params CommandOption[] options) =>
        new(parent, name, null, description, options, (_, _) => Task.FromResult(0));

    /// <summary>The unnamed command an application runs when given no command at all.</summary>
    public static CommandDefinition RootCommand(params CommandOption[] options) =>
        new(null, "", null, "", options, (_, _) => Task.FromResult(0));

    public static CommandOption Required(
        string name,
        CommandOptionType type = CommandOptionType.String,
        string description = "") =>
        new(name, type, description, true, false);

    public static CommandOption Optional(
        string name,
        CommandOptionType type = CommandOptionType.String,
        string description = "") =>
        new(name, type, description, false, false);

    public static CommandOption Array(
        string name,
        CommandOptionType type = CommandOptionType.String,
        bool required = true) =>
        new(name, type, "", required, true);

    private sealed class StubProvider(IReadOnlyList<CommandDefinition> definitions)
        : ICommandDefinitionProvider {

        public IEnumerable<CommandDefinition> ProvideDefinitions() => definitions;
    }
}
