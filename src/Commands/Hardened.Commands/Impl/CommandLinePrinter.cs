using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Attributes;
using Microsoft.Extensions.Options;
using System.Reflection;
using System.Text;

namespace Hardened.Commands.Impl;

public interface ICommandLinePrinter {
    Task PrintParseResult(ParseResult result);
}

[Expose]
public class CommandLinePrinter : ICommandLinePrinter {
    private readonly ICommandLineDefinitionService _commandLineDefinitionService;
    private readonly IEnvironment _environment;
    private readonly IConsoleOutputService _consoleOutputService;
    private readonly IOptions<CommandLineParserOptions> _options;
    
    public CommandLinePrinter(
        ICommandLineDefinitionService commandLineDefinitionService,
        IEnvironment environment, 
        IConsoleOutputService consoleOutputService,
        IOptions<CommandLineParserOptions> options) {
        _commandLineDefinitionService = commandLineDefinitionService;
        _environment = environment;
        _consoleOutputService = consoleOutputService;
        _options = options;
    }

    public async Task PrintParseResult(ParseResult result) {
        var commands = _commandLineDefinitionService.GetTree();
        Console.WriteLine();
        
        switch (result.ResultStatus) {
            case ParseResultStatus.Help:
                await PrintHelp(result, commands);
                break;
            case ParseResultStatus.MissingOption:
                await PrintMissingOption(result, commands);
                break;
            case ParseResultStatus.InvalidOption:
                await PrintInvalidOption(result, commands);
                break;
            case ParseResultStatus.NoCommandFound:
                await PrintNoCommandFound(result, commands);
                break;
            case ParseResultStatus.NoSubCommandProvided:
                await PrintNoSubCommandProvided(result, commands);
                break;
        }
        
        Console.WriteLine();
    }

    private async Task PrintNoSubCommandProvided(ParseResult result, CommandTreeNode commands) {
        if (result.CommandTreeNode != null) {
            Console.WriteLine();
            
            Console.WriteLine(
                $"Missing subcommand for command {result.CommandTreeNode.Command.CommandName}");
        }

        Console.WriteLine();
        
        await WriteCommandHelp(result, commands);
    }

    protected virtual async Task PrintNoCommandFound(ParseResult result, CommandTreeNode commands) {
        var unknownCommand = _environment.Arguments.FirstOrDefault();
        
        if (result.CommandTreeNode != null && _environment.Arguments.Count >= 2) {
            unknownCommand = _environment.Arguments[1];
            Console.WriteLine($"{unknownCommand} is not a valid subcommand of {result.CommandTreeNode.Command.CommandName}");
        }
        else {
            Console.WriteLine($"{unknownCommand} command not found");
        }

        Console.WriteLine();
        
        await WriteCommandHelp(result, commands);
    }


    protected virtual async Task PrintInvalidOption(ParseResult result, CommandTreeNode commands) {
        _consoleOutputService.WriteLine($"Invalid option: {result.InvalidOption}");
        _consoleOutputService.WriteLine("");
        
        await WriteCommandOptionsHelp(result, commands);
    }

    protected virtual async Task PrintMissingOption(ParseResult result, CommandTreeNode commands) {
        var missingOptionString = new StringBuilder();

        if (result.MissingOptions != null) {
            foreach (var missingOption in result.MissingOptions) {
                missingOptionString.Append(_options.Value.OptionPrefix);
                missingOptionString.Append(missingOption.OptionName);
                missingOptionString.Append(" ");
            }
        }

        _consoleOutputService.WriteLine($"Missing options: {missingOptionString}");
        _consoleOutputService.WriteLine("");
        
        await WriteCommandOptionsHelp(result, commands);
    }

    protected virtual async Task PrintHelp(ParseResult result, CommandTreeNode commands) {

        if (commands.ChildCommands.Count > 0) {
            await WriteCommandHelp(result, commands);
        }
        else {
            await WriteCommandOptionsHelp(result, commands);
        }
    }

    private async Task WriteCommandOptionsHelp(ParseResult result, CommandTreeNode commands) {
        var entryPointName = Assembly.GetEntryAssembly()?.GetName().Name ?? "entrypoint";

        var usageCommand = GetCommands(result.CommandTreeNode);

        _consoleOutputService.WriteLine("Usage:");
        _consoleOutputService.WriteLine($"    {entryPointName} {usageCommand} [options]");
        _consoleOutputService.WriteLine("Options:");

        var options = new List<CommandOption>();
        
        GetAllOptions(result.CommandTreeNode ?? commands, options);

        var maxOptionSize = options.Select(o => o.OptionName.Length).Max();
        
        foreach (var option in options) {
            var optionName = _options.Value.OptionPrefix + option.OptionName;

            var padString = new string(' ', maxOptionSize - option.OptionName.Length);
            var requiredString = option.IsRequired ? "[required]" : "[optional]";
            
            WriteWrappingLine($"    {optionName}{padString}  {requiredString}  ",option.Description);
        }
    }

    private void GetAllOptions(CommandTreeNode commandTree, List<CommandOption> allOptions) {
        allOptions.AddRange(commandTree.Command.Options);

        if (commandTree.ParentCommand != null) {
            GetAllOptions(commandTree.ParentCommand, allOptions);
        }
    }
    
    private string GetCommands(CommandTreeNode? resultCommandTreeNode) {
        var returnString = "";

        if (resultCommandTreeNode != null) {
            returnString = resultCommandTreeNode.Command.CommandName;

            if (resultCommandTreeNode.ParentCommand != null) {
                var parentName = GetCommands(resultCommandTreeNode.ParentCommand);

                if (!string.IsNullOrEmpty(parentName)) {
                    returnString = $"{parentName} {returnString}";
                }
            }
        }
        
        return returnString;
    }

    protected virtual async Task WriteCommandHelp(ParseResult result, CommandTreeNode commands) {
        var entryPointName = Assembly.GetEntryAssembly()?.GetName().Name ?? "entrypoint";
        
        var usageCommand = "";

        if (result.CommandTreeNode == null) {
            usageCommand = "<command>";
    
            if (!string.IsNullOrEmpty(commands.ChildCommands.FirstOrDefault()?.ChildCommands
                    .FirstOrDefault()?.Command.CommandName)) {
                usageCommand = "<command> <subcommand>";
            }
        }
        else {
            usageCommand = $"{result.CommandTreeNode.Command.CommandName} <subcommand>";
        }

        _consoleOutputService.WriteLine("Usage:");
        _consoleOutputService.WriteLine($"    {entryPointName} {usageCommand} [options]");
        _consoleOutputService.WriteLine("Commands:");

        var childCommands = commands.ChildCommands;

        if (result.CommandTreeNode != null) {
            var matchingNode =
                commands.ChildCommands.FirstOrDefault(node =>
                    node.Command.CommandName == result.CommandTreeNode.Command.CommandName);

            if (matchingNode != null) {
                childCommands = matchingNode.ChildCommands;
            }
        }
        
        if (childCommands.Count > 0) {
            var commandMaxLength = 
                childCommands.Select(n => n.Command.CommandName.Length).Max();
            
            foreach (var command in childCommands) {
                var commandString = $"    {command.Command.CommandName}";
                var padLength = commandMaxLength - command.Command.CommandName.Length;

                padLength += 4;
                commandString += new string(' ', padLength);

                WriteWrappingLine(commandString, command.Command.Description);
            }
        }
    }

    protected virtual void WriteWrappingLine(string commandString, string commandDescription) {
        var splitString = commandDescription.Split(' ').ToList();
        var outputString = new StringBuilder(commandString);
        
        while (splitString.Count > 0) {
            while (splitString.Count > 0) {
                if (splitString[0].Length + outputString.Length >= 80) {
                    // this handles the infinite loop case
                    if (commandString.Length != outputString.Length) {
                        break;
                    }
                }

                outputString.Append(' ');
                outputString.Append(splitString[0]);
                splitString.RemoveAt(0);
            }
            
            _consoleOutputService.WriteLine(outputString.ToString());
            outputString.Clear();
        }
    }
}