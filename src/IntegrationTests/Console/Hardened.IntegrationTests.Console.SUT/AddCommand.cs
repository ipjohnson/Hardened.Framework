using DependencyModules.Runtime.Attributes;
using Hardened.Commands;
using Hardened.Commands.Attributes;

namespace Hardened.IntegrationTests.Console.SUT;

[Command("add", Description = "Add two numbers together", ParentCommand = "math")]
public class AddCommand : MathCommand{
}

[TransientService]
public class AddCommandHandler : ICommandHandler<AddCommand> {
    public async Task<int> Handle(AddCommand value) {
        await System.Console.Out.WriteLineAsync($"{value.X} + {value.Y} = {value.X + value.Y}");

        return 0;
    }
}