using DependencyModules.Runtime.Attributes;

namespace Hardened1;

/// <summary>What this library does, as a consumer sees it.</summary>
public interface IGreetingService {
    string Greet(string name);
}

/// <summary>
/// Registered by the attribute on the class, rather than by a line in the module.
/// </summary>
/// <remarks>
/// [SingletonService] registers this against every interface it implements. [ScopedService] and
/// [TransientService] are the other two lifetimes and behave the same way.
///
/// The dependency arrives through the constructor with nothing declaring it anywhere else - the
/// generator resolved it at build time, and a missing registration is a build error rather than
/// something the first request finds out.
/// </remarks>
[SingletonService]
public class GreetingService(IGreetingFormatter formatter) : IGreetingService {

    public string Greet(string name) => formatter.Format($"Hello, {name}");
}
