using Hardened.Functions.Testing;
using Hardened.IntegrationTests.Sqs.SUT;
using Hardened.Requests.Abstract.Execution;
using Xunit;

namespace Hardened.IntegrationTests.Sqs.SUT.Tests;

/// <summary>
/// What the harness will and will not construct.
///
/// <para>
/// The rule is a constructor taking <see cref="TriggerSend"/> or <see cref="TriggerCall"/>, and
/// those being named delegates rather than <c>Func</c> shapes is the whole of why it is safe. A
/// structural match would also match anything with the same shape, and a test parameter that
/// happened to have one would be handed a delegate it never asked for and fail somewhere strange.
/// </para>
/// </summary>
public class FacadeRecognitionTests {

    [Fact]
    public void AGeneratedFacadeIsRecognised() {
        Assert.NotNull(TriggerInvoker.Constructor(typeof(SqsTestApp.Queues)));
    }

    /// <summary>
    /// The case the named delegate exists for. This type has exactly the shape a <c>Func</c> match
    /// would accept, and it is not a façade.
    /// </summary>
    [Fact]
    public void AConstructorOfTheSameShapeIsNotAFacade() {
        Assert.Null(TriggerInvoker.Constructor(typeof(LooksLikeOne)));
    }

    [Fact]
    public void AnOrdinaryTypeIsNotAFacade() {
        Assert.Null(TriggerInvoker.Constructor(typeof(Order)));
    }

    /// <summary>
    /// Something a test author might plausibly write: a helper taking a send callback. Same three
    /// parameters, same return, and nothing to do with this harness.
    /// </summary>
    private sealed class LooksLikeOne {
        public LooksLikeOne(Func<object, string, string, Task> send) {
            Send = send;
        }

        public Func<object, string, string, Task> Send { get; }
    }
}
