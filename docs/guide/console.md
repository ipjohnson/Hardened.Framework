# Console commands

The same module system builds command line applications. A command is a class describing its
arguments; a handler is a class that runs it. The parser, the help text and the argument binding are
generated.

## An application

```csharp
using Hardened.Commands;
using Hardened.Shared.Runtime.Attributes;

[HardenedModule]
[CommandsLibrary]
public partial class Application { }
```

```csharp
// Program.cs
var application = new Application(args);

var result = await application.Run();

await application.DisposeAsync();

return result;
```

`Application` here is a [self-hosting entry point](/guide/modules#self-hosting-entry-points): the
generator writes the constructors, the service provider and `Run()`. `Run()` returns the process
exit code, and `DisposeAsync` shuts the provider down.

## A command

The class is the argument shape. Public properties become options:

```csharp
using Hardened.Commands.Attributes;

[Command("math", Description = "Collection of math related commands")]
public class MathCommand {
    public int X { get; set; }

    public int Y { get; set; }
}
```

## Subcommands

`ParentCommand` nests one command under another. Inheriting the parent's class inherits its options:

```csharp
[Command("add", Description = "Add two numbers together", ParentCommand = "math")]
public class AddCommand : MathCommand { }
```

```
$ myapp math add --x 2 --y 3
2 + 3 = 5
```

## A handler

One handler per command, registered like any other service:

```csharp
using DependencyModules.Runtime.Attributes;
using Hardened.Commands;

[TransientService]
public class AddCommandHandler : ICommandHandler<AddCommand> {
    public async Task<int> Handle(AddCommand value) {
        await Console.Out.WriteLineAsync($"{value.X} + {value.Y} = {value.X + value.Y}");

        return 0;
    }
}
```

```csharp
public interface ICommandHandler<in T> {
    Task<int> Handle(T value);
}
```

The returned `int` becomes the process exit code, so a handler reports failure the way a command line
tool is expected to. The handler is resolved from the container, so it takes whatever dependencies it
needs through its constructor.

## Options

Properties are options by default. The attributes adjust the details:

```csharp
[Command("import")]
public class ImportCommand {
    [Option(Name = "source", Description = "Path to the file to import")]
    public string Input { get; set; } = "";

    [FileOption(Name = "config", Description = "Configuration file to read")]
    public string ConfigPath { get; set; } = "";

    [ExcludeOption]
    public string ComputedInternally { get; set; } = "";
}
```

| Attribute | Effect |
|---|---|
| `[Option]` | Renames the option, or gives it help text |
| `[FileOption]` | An option whose value is a path, described as such in help |
| `[ExcludeOption]` | A property that is not an option — it will not be parsed or listed |

`Description` on `[Command]`, `[Option]` and `[FileOption]` is what the generated help prints, so
filling it in is the whole of documenting the tool.
