namespace Hardened.Commands.Attributes;

public class CommandAttribute : Attribute {
    public CommandAttribute(string command) {
        Command = command;
    }

    public string Command { get; }

    public string? ParentCommand { get; set; }

    public string? Description { get; set; }
}