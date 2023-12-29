using Hardened.Shared.Runtime.Attributes;

namespace Hardened.Commands.Impl;

public interface IConsoleOutputService {
    void WriteLine(string line);
}

[Expose]
[Singleton]
public class ConsoleOutputService : IConsoleOutputService {
    public void WriteLine(string line) {
        Console.WriteLine(line);
    }
}