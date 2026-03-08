using DependencyModules.Runtime.Attributes;

namespace Hardened.Commands.Impl;

public interface IConsoleOutputService {
    void WriteLine(string line);
}

[SingletonService]
public class ConsoleOutputService : IConsoleOutputService {
    public void WriteLine(string line) {
        Console.WriteLine(line);
    }
}