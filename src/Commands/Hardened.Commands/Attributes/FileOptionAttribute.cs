namespace Hardened.Commands.Attributes;

public class FileOptionAttribute : Attribute {
    public string? Name { get; set; }
    
    public string? Description { get; set; }
}