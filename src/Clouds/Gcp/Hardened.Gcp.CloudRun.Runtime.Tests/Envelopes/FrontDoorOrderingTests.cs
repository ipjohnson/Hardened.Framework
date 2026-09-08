using Hardened.Gcp.CloudRun.Runtime.Envelopes;
using Hardened.Gcp.CloudRun.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Xunit;

namespace Hardened.Gcp.CloudRun.Runtime.Tests.Envelopes;

/// <summary>
/// A fallback envelope is asked after every ordinary one, whatever order they were registered in.
/// </summary>
public class FrontDoorOrderingTests {

    [Fact]
    public void FallbacksAreAskedLastWhateverTheRegistrationOrder() {
        var generic = new Generic();
        var specific = new Specific();

        var frontDoor = new TriggerFrontDoor([generic, specific]);

        Assert.Equal([specific, generic], frontDoor.Envelopes);
    }

    [Fact]
    public void OrdinaryEnvelopesKeepTheirRegistrationOrder() {
        var first = new Specific();
        var second = new Specific();

        var frontDoor = new TriggerFrontDoor([first, new Generic(), second]);

        Assert.Same(first, frontDoor.Envelopes[0]);
        Assert.Same(second, frontDoor.Envelopes[1]);
        Assert.IsType<Generic>(frontDoor.Envelopes[2]);
    }

    private sealed class Specific : ITriggerEnvelope {
        public bool Recognises(IExecutionRequest request) => true;
        public CloudRunTriggerRequest? Unwrap(IExecutionRequest request, TriggerPayload payload) => null;
    }

    private sealed class Generic : IFallbackTriggerEnvelope {
        public bool Recognises(IExecutionRequest request) => true;
        public CloudRunTriggerRequest? Unwrap(IExecutionRequest request, TriggerPayload payload) => null;
    }
}
