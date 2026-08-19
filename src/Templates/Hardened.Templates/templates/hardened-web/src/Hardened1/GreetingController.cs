using Hardened.Web.Runtime.Attributes;

namespace Hardened1;

/// <summary>
/// A plain class. No base type, no interface, no registration - the generator finds the route
/// attribute at build time and emits a handler bound to this method's exact signature.
/// </summary>
public class GreetingController {

    /// <summary>Greets someone by name.</summary>
    [Get("/{name}")]
    public Greeting Hello(string name) => new($"Hello, {name}!");
}

/// <summary>What the route returns. A model rather than a bare string, so the generated
/// OpenAPI document describes a schema.</summary>
public record Greeting(string Message);
