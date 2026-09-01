using DependencyModules.Runtime.Attributes;

namespace Hardened.IntegrationTests.WebApp.SUT.Services;

public interface IEnvironmentGatedService {
    string Environment { get; }
}

/// <summary>
/// Registered only under the environment the test names, so a test annotated
/// <c>[EnvironmentName("environment-gated")]</c> can prove the gate reads the test's environment
/// rather than a default. No handler uses this; the registration is the feature under test.
/// </summary>
[SingletonService]
[IfEnvironment("environment-gated")]
public class EnvironmentGatedService : IEnvironmentGatedService {
    public string Environment => "environment-gated";
}
