using Hardened.Shared.Runtime.Attributes;

namespace Hardened.Commands.Impl;

public record CommandTreeNode(
    CommandDefinition Command,
    IReadOnlyList<CommandTreeNode> ChildCommands) {
    public CommandTreeNode? ParentCommand { get; set; }
};

public interface ICommandLineDefinitionService {
    CommandTreeNode GetTree();
}

[Expose]
public class CommandLineDefinitionService : ICommandLineDefinitionService {
    private readonly IReadOnlyList<ICommandDefinitionProvider> _definitionProviders;
    private CommandTreeNode? _commandTreeNode;

    public CommandLineDefinitionService(IEnumerable<ICommandDefinitionProvider> definitionProviders) {
        _definitionProviders = definitionProviders.ToList();
    }

    public CommandTreeNode GetTree() {
        if (_commandTreeNode != null) {
            return _commandTreeNode;
        }

        var commands = GetAllCommands();

        var rootCommand = GetRootCommand(commands) ??
                          new CommandDefinition(
                              null,
                              "",
                              null,
                              "",
                              ArraySegment<CommandOption>.Empty,
                              null);

        return _commandTreeNode = GenerateTree(commands, rootCommand);
    }

    private CommandTreeNode GenerateTree(IReadOnlyList<CommandDefinition> commands, CommandDefinition rootCommand) {
        var treeDictionary = new Dictionary<string, Tuple<CommandDefinition, List<CommandTreeNode>>>();

        foreach (var command in commands.Where(c => string.IsNullOrEmpty(c.ParentCommandName))) {
            if (treeDictionary.ContainsKey(command.CommandName)) {
                throw new Exception($"Duplicate {command.CommandName} command found");
            }

            treeDictionary[command.CommandName] =
                new Tuple<CommandDefinition, List<CommandTreeNode>>(command, new List<CommandTreeNode>());
        }

        foreach (var command in commands.Where(c => !string.IsNullOrEmpty(c.ParentCommandName))) {
            if (treeDictionary.TryGetValue(command.ParentCommandName!, out var tuple)) {
                tuple.Item2.Add(new CommandTreeNode(command, ArraySegment<CommandTreeNode>.Empty));
            }
            else {
                throw new Exception($"Parent command {command.ParentCommandName} not found");
            }
        }

        var commandNode = new CommandTreeNode(rootCommand,
            treeDictionary.Values.Select(t => new CommandTreeNode(t.Item1, t.Item2)).ToList());

        foreach (var childCommand in commandNode.ChildCommands) {
            foreach (var treeNode in childCommand.ChildCommands) {
                treeNode.ParentCommand = childCommand;
            }
            childCommand.ParentCommand = commandNode;
        }
        
        return commandNode;
    }

    private IReadOnlyList<CommandDefinition> GetAllCommands() {
        var results = new List<CommandDefinition>();

        foreach (var provider in _definitionProviders) {
            results.AddRange(provider.ProvideDefinitions());
        }

        return results;
    }

    private CommandDefinition? GetRootCommand(IReadOnlyList<CommandDefinition> commands) {
        var rootCommands = commands.Where(command => string.IsNullOrEmpty(command.CommandName)).ToArray();

        if (rootCommands.Length > 1) {
            throw new Exception("Only one root command can exist, found " + rootCommands.Length);
        }

        return rootCommands.FirstOrDefault();
    }
}