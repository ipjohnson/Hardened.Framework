namespace Hardened.Commands.Attributes;

public class OptionAttribute : Attribute {
    public string? Name { get; set; }
    
    public string? Description { get; set; }
}