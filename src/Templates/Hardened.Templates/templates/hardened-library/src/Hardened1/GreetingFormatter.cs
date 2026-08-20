using DependencyModules.Runtime.Attributes;

namespace Hardened1;

/// <summary>
/// A second service, so the library has something to inject and the tests have something to
/// substitute.
/// </summary>
public interface IGreetingFormatter {
    string Format(string message);
}

[SingletonService]
public class GreetingFormatter : IGreetingFormatter {

    public string Format(string message) => message + "!";
}
