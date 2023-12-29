using Hardened.Commands.Attributes;

namespace Hardened.IntegrationTests.Console.SUT;

[Command("math", Description = "Collection of math related commands")]
public class MathCommand {
    public int X { get; set; }
    
    public int Y { get; set; }
}