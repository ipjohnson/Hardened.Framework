#if (nsubstitute)
using NSubstitute;
#endif
#if (moq)
using Moq;
#endif
#if (fakeiteasy)
using FakeItEasy;
#endif

namespace Hardened1.Tests;

/// <summary>
/// [HardenedTest] boots the module and resolves the test's parameters from its container, so what
/// runs is the registration a consuming application would get rather than a fresh `new`.
/// </summary>
public class GreetingServiceTests {

    [HardenedTest]
    public void GreetsByName(IGreetingService greeting) {
#if (xunit)
        Assert.Equal("Hello, world!", greeting.Greet("world"));
#else
        Assert.That(greeting.Greet("world"), Is.EqualTo("Hello, world!"));
#endif
    }

    /// <summary>
#if (moq)
    /// A Mock&lt;T&gt; parameter substitutes a service for the whole container, including behind
    /// another service. The parameter is the mock to configure; the container holds its Object.
#else
    /// [Mock] substitutes a service for the whole container, including behind another service.
#endif
    /// </summary>
    /// <remarks>
    /// IGreetingService here is the real registration, resolved from the module - and it used the
    /// substitute, because the substitution happened in the container rather than in this test.
    /// That is what makes it worth writing: the wiring under test is the application's.
    /// </remarks>
    [HardenedTest]
#if (moq)
    public void ASubstitutedDependencyIsUsedByTheRealService(
        IGreetingService greeting,
        Mock<IGreetingFormatter> formatter) {
        formatter.Setup(f => f.Format(It.IsAny<string>())).Returns("substituted");
#else
    public void ASubstitutedDependencyIsUsedByTheRealService(
        IGreetingService greeting,
        [Mock] IGreetingFormatter formatter) {
#if (nsubstitute)
        formatter.Format(Arg.Any<string>()).Returns("substituted");
#endif
#if (fakeiteasy)
        A.CallTo(() => formatter.Format(A<string>._)).Returns("substituted");
#endif
#endif

#if (xunit)
        Assert.Equal("substituted", greeting.Greet("world"));
#else
        Assert.That(greeting.Greet("world"), Is.EqualTo("substituted"));
#endif
    }
}
