using Amazon.Lambda.SQSEvents;
using Hardened.Amz.Function.Sqs.Runtime.Tests.Infrastructure;
using Hardened.Requests.Abstract.Execution;
using static Hardened.Amz.Function.Sqs.Runtime.Tests.Infrastructure.SqsBatchHarness;

namespace Hardened.Amz.Function.Sqs.Runtime.Tests.Impl;

/// <summary>
/// How a single SQS message becomes a request. The handler is a plain Hardened function that knows
/// nothing about SQS, so the message body has to arrive as the request body, byte for byte.
/// </summary>
public class SqsBatchFilterRecordTests {

    [Fact]
    public async Task TheMessageBodyArrivesAsTheRequestBodyVerbatim() {
        var bodies = new List<string>();

        await Run([Message("m1", "{\"order\":\"A-1\",\"qty\":3}")], (_, body) => {
            bodies.Add(body);

            return Task.CompletedTask;
        });

        Assert.Equal("{\"order\":\"A-1\",\"qty\":3}", Assert.Single(bodies));
    }

    /// <summary>
    /// An SQS body is a string, not JSON — the filter must not re-serialise it. A body that is not
    /// JSON at all has to reach the handler unchanged so the handler's own deserialisation is what
    /// fails, and the message is named in the batch response.
    /// </summary>
    [Fact]
    public async Task ABodyThatIsNotJsonReachesTheHandlerUnchanged() {
        var bodies = new List<string>();

        await Run([Message("m1", "not json at all")], (_, body) => {
            bodies.Add(body);

            return Task.CompletedTask;
        });

        Assert.Equal("not json at all", Assert.Single(bodies));
    }

    /// <summary>
    /// SQS message bodies are UTF-8. Writing them through any other encoding corrupts anything
    /// outside ASCII, which shows up as a deserialisation failure on a customer name rather than as
    /// anything obviously encoding-shaped.
    /// </summary>
    [Fact]
    public async Task ANonAsciiBodySurvivesAsUtf8() {
        var bodies = new List<string>();

        await Run([Message("m1", "{\"name\":\"Ünicode ✓ 日本語\"}")], (_, body) => {
            bodies.Add(body);

            return Task.CompletedTask;
        });

        Assert.Equal("{\"name\":\"Ünicode ✓ 日本語\"}", Assert.Single(bodies));
    }

    [Fact]
    public async Task EveryMessageInTheBatchReachesTheHandlerInOrder() {
        var bodies = new List<string>();

        await Run(
            [Message("m1", "one"), Message("m2", "two"), Message("m3", "three")],
            (_, body) => {
                bodies.Add(body);

                return Task.CompletedTask;
            });

        Assert.Equal(new[] { "one", "two", "three" }, bodies);
    }

    /// <summary>
    /// Each message gets a stream of its own, rewound to the start. A pooled stream carried over
    /// from the previous message would leave the second handler reading the first message's body.
    /// </summary>
    [Fact]
    public async Task AShortMessageAfterALongOneDoesNotSeeTheLongOnesTail() {
        var bodies = new List<string>();

        await Run(
            [Message("m1", new string('x', 512)), Message("m2", "short")],
            (_, body) => {
                bodies.Add(body);

                return Task.CompletedTask;
            });

        Assert.Equal("short", bodies[1]);
    }

    [Fact]
    public async Task EachMessageRunsAgainstAResponseOfItsOwn() {
        var responses = new List<IExecutionResponse>();

        await Run([Message("m1", "one"), Message("m2", "two")], (forked, _) => {
            responses.Add(forked.Response);

            return Task.CompletedTask;
        });

        Assert.Equal(2, responses.Distinct().Count());
    }

    /// <summary>
    /// The request method and path are inherited from the invocation, so a handler bound to the
    /// function name still resolves for every message in the batch.
    /// </summary>
    [Fact]
    public async Task EachMessageKeepsTheInvocationsMethodAndPath() {
        var requests = new List<IExecutionRequest>();

        await Run([Message("m1", "one"), Message("m2", "two")], (forked, _) => {
            requests.Add(forked.Request);

            return Task.CompletedTask;
        });

        Assert.All(requests, r => Assert.Equal("Invoke", r.Method));
        Assert.All(requests, r => Assert.Equal("TestFunction", r.Path));
    }

    [Fact]
    public async Task AnEventWithNoRecordsNeverReachesTheHandler() {
        var invoked = false;

        await Run(Array.Empty<SQSEvent.SQSMessage>(), (_, _) => {
            invoked = true;

            return Task.CompletedTask;
        });

        Assert.False(invoked);
    }
}
