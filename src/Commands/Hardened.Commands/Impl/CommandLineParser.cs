using Hardened.Shared.Runtime.Attributes;
using Microsoft.Extensions.Options;

namespace Hardened.Commands.Impl;

public enum ParseResultStatus {
    Success,
    Help,
    Version,
    NoCommandFound,
    NoSubCommandProvided,
    MissingOption,
    InvalidOption
}

public record ParseResult(
    ParseResultStatus ResultStatus,
    CommandTreeNode? CommandTreeNode,
    IReadOnlyDictionary<string, string[]> Options,
    string? ParseError,
    IReadOnlyList<CommandOption>? MissingOptions,
    string? InvalidOption = null);

public interface ICommandLineParser {
    Task<ParseResult> ParseCommandLineArguments(IReadOnlyList<string> arguments);
}

[Expose]
public class CommandLineParser : ICommandLineParser {
    private readonly ICommandLineDefinitionService _commandLineDefinitionService;
    private readonly IOptions<CommandLineParserOptions> _options;

    public CommandLineParser(
        ICommandLineDefinitionService commandLineDefinitionService,
        IOptions<CommandLineParserOptions> options) {
        _commandLineDefinitionService = commandLineDefinitionService;
        _options = options;
    }

    public async Task<ParseResult> ParseCommandLineArguments(IReadOnlyList<string> arguments) {
        var commandTree = _commandLineDefinitionService.GetTree();

        var isCommandBased = IsArgumentCommand(arguments);

        if (!isCommandBased) {
            return ParseNoCommandArguments(arguments, commandTree);
        }

        return ParseCommandBasedArgumentString(arguments, 0, commandTree);
    }

    private ParseResult ParseCommandBasedArgumentString(
        IReadOnlyList<string> arguments,
        int index,
        CommandTreeNode commandTree) {
        var argument = arguments[index].ToLower();

        if (argument.StartsWith(_options.Value.OptionPrefix)) {
            return new ParseResult(ParseResultStatus.NoCommandFound, null,
                new Dictionary<string, string[]>(), "Could not find command", null);
        }

        var command =
            commandTree.ChildCommands.FirstOrDefault(c => c.Command.CommandName == argument);

        if (command == null) {
            if (commandTree.ParentCommand == null) {
                return new ParseResult(ParseResultStatus.NoCommandFound, null,
                    new Dictionary<string, string[]>(), "Could not find command", null);
            }

            return new ParseResult(ParseResultStatus.NoCommandFound, commandTree,
                new Dictionary<string, string[]>(), "Could not find sub command", null);
        }

        if (command.ChildCommands.Count > 0) {
            if (arguments.Count == index + 1) {
                return new ParseResult(ParseResultStatus.NoSubCommandProvided, command,
                    new Dictionary<string, string[]>(), "Missing subcommand", null);
            }

            return ParseCommandBasedArgumentString(arguments, index + 1, command);
        }

        var argSpace = arguments.ToArray().AsSpan();

        return ParseOptions(command, argSpace.Slice(index + 1).ToArray());
    }

    private ParseResult ParseNoCommandArguments(
        IReadOnlyList<string> arguments,
        CommandTreeNode commandTree) {
        if (commandTree.Command.RunCommandDelegate == null) {
            return new ParseResult(ParseResultStatus.Help, null, new Dictionary<string, string[]>(),
                "", null);
        }

        return ParseOptions(commandTree, arguments);
    }

    private ParseResult ParseOptions(CommandTreeNode commandTree, IReadOnlyList<string> arguments) {
        var parseOptions = new Dictionary<string, string[]>();
        var result = ParseResultStatus.Success;
        var parseError = "";
        var allOptions = new List<CommandOption>();
        GetAllOptions(commandTree, allOptions);
        IReadOnlyList<CommandOption>? missingOptions = null;
        string? invalidOption = null;
        
        for (var index = 0; index < arguments.Count;) {
            var argument = arguments[index].ToLower();
            if (!argument.StartsWith(_options.Value.OptionPrefix)) {
                result = ParseResultStatus.InvalidOption;
                parseError = $"Invalid option ${arguments[index]} found";
                break;
            }

            var found = false;
            foreach (var option in allOptions) {
                var prefixedOption = _options.Value.OptionPrefix + option.OptionName.ToLower();

                if (argument == prefixedOption) {
                    allOptions.Remove(option);
                    var optionValues = new List<string>();

                    if (option.IsArray) {
                        if (index + 1 < arguments.Count) {
                            index++;
                            for (;
                                 index < arguments.Count;
                                 index++) {
                                if (arguments[index].StartsWith(_options.Value.OptionPrefix)) {
                                    break;
                                }
                                else {
                                    optionValues.Add(arguments[index]);
                                }
                            }
                        }
                    }
                    else {
                        if (index + 1 > arguments.Count) {
                            parseError = $"Option ${option.OptionName} missing value";
                            result = ParseResultStatus.InvalidOption;
                        }
                        else {
                            optionValues.Add(arguments[index + 1]);
                        }

                        index += 2;
                    }

                    parseOptions[option.OptionName] = optionValues.ToArray();
                    found = true;
                    break;
                }
            }

            if (!found) {
                result = argument == _options.Value.OptionPrefix + "help"
                    ? ParseResultStatus.Help
                    : ParseResultStatus.InvalidOption;

                if (result == ParseResultStatus.InvalidOption) {
                    invalidOption = argument;
                }
                
                break;
            }
        }

        if (result == ParseResultStatus.Success && 
            allOptions.Any(o => o.IsRequired)) {
            missingOptions = allOptions.Where(o => o.IsRequired).ToArray();
            result = ParseResultStatus.MissingOption;
        }

        return new ParseResult(result, commandTree, parseOptions, parseError,
            missingOptions, invalidOption);
    }

    private void GetAllOptions(CommandTreeNode commandTree, List<CommandOption> allOptions) {
        allOptions.AddRange(commandTree.Command.Options);

        if (commandTree.ParentCommand != null) {
            GetAllOptions(commandTree.ParentCommand, allOptions);
        }
    }

    private bool IsArgumentCommand(IReadOnlyList<string> arguments) {
        return arguments.Count > 0 && !arguments[0].StartsWith(_options.Value.OptionPrefix);
    }
}