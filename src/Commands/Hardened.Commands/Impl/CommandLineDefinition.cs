namespace Hardened.Commands.Impl;

public enum CommandOptionType {
    String,
    File,
    Number,
    Boolean
}

public record CommandOption(string OptionName, CommandOptionType OptionType, string Description, bool IsRequired, bool IsArray);

public delegate Task<int> RunCommandDelegate(IServiceProvider serviceProvider,
    IReadOnlyDictionary<string, string[]> options);

public record CommandDefinition(
    string? ParentCommandName,
    string CommandName,
    string? ShortName,
    string Description,
    IReadOnlyList<CommandOption> Options,
    RunCommandDelegate? RunCommandDelegate);

public interface ICommandDefinitionProvider {
    IEnumerable<CommandDefinition> ProvideDefinitions();
}