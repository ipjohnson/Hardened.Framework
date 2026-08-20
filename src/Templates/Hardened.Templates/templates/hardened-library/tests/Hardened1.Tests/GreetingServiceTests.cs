using NSubstitute;

namespace Hardened1.Tests;

/// <summary>
/// [HardenedTest] boots the module and resolves the test's parameters from its container, so what
/// runs is the registration a consuming application would get rather than a fresh `new`.
/// </summary>
public class GreetingServiceTests {

    [HardenedTest]
    public void GreetsByName(IGreetingService greeting) {
        Assert.Equal("Hello, world!", greeting.Greet("world"));
    }

    /// <summary>
    /// [Mock] substitutes a service for the whole container, including behind another service.
    /// </summary>
    /// <remarks>
    /// IGreetingService here is the real registration, resolved from the module - and it used the
    /// substitute, because the substitution happened in the container rather than in this test.
    /// That is what makes it worth writing: the wiring under test is the application's.
    /// </remarks>
    [HardenedTest]
    public void ASubstitutedDependencyIsUsedByTheRealService(
        IGreetingService greeting,
        [Mock] IGreetingFormatter formatter) {
        formatter.Format(Arg.Any<string>()).Returns("substituted");

        Assert.Equal("substituted", greeting.Greet("world"));
    }
}
