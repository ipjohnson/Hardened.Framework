using System.Text;
using Hardened.Gcp.CloudRun.Runtime.Envelopes;
using Hardened.Gcp.CloudRun.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.QueryString;
using Hardened.Requests.Testing;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using Xunit;

namespace Hardened.Gcp.CloudRun.Runtime.Tests.Envelopes;

/// <summary>
/// What the front door does with a request: passes it on, gives its body back, or forks.
/// </summary>
public class TriggerFrontDoorTests {

    [Fact]
    public async Task ARequestNoEnvelopeRecognisesGoesOnUntouched() {
        var harness = new Harness("POST", "text/plain", "hello");
        var envelope = new StubEnvelope(recognises: false);

        await new TriggerFrontDoor([envelope]).Execute(harness.Chain);

        await harness.Chain.Received(1).Next();
        harness.Chain.DidNotReceive().Fork(Arg.Any<IExecutionContext>());
        Assert.Equal(0, envelope.Unwrapped);
        Assert.Equal(0, harness.Request.Body.Position);
    }

    /// <summary>A JSON POST that is not a push after all is served as if nothing had looked: the bytes are back, rewound.</summary>
    [Fact]
    public async Task ARecognisedRequestThatIsNotAnEnvelopeGetsItsBodyBack() {
        var harness = new Harness("POST", "application/json", "{\"title\":\"a todo\"}");

        await new TriggerFrontDoor([new StubEnvelope(recognises: true)]).Execute(harness.Chain);

        await harness.Chain.Received(1).Next();

        using var reader = new StreamReader(harness.Request.Body);

        Assert.Equal("{\"title\":\"a todo\"}", reader.ReadToEnd());
    }

    [Fact]
    public async Task AnUnwrappedEnvelopeForksTheChainAndDoesNotContinueTheOriginal() {
        var harness = new Harness("POST", "application/json", "{\"message\":{}}");
        var envelope = new StubEnvelope(recognises: true, unwrap: (delivery, _) => Trigger(delivery));

        await new TriggerFrontDoor([envelope]).Execute(harness.Chain);

        await harness.Forked.Received(1).Next();
        await harness.Chain.DidNotReceive().Next();

        var trigger = Assert.IsType<CloudRunTriggerRequest>(harness.ForkedContext!.Request);

        Assert.Equal("QUEUE", trigger.Method);
        Assert.Equal("/orders", trigger.Path);
    }

    /// <summary>The first envelope to unwrap wins; the ones after it are not asked.</summary>
    [Fact]
    public async Task TheFirstEnvelopeToUnwrapWins() {
        var harness = new Harness("POST", "application/json", "{}");
        var declines = new StubEnvelope(recognises: true);
        var first = new StubEnvelope(recognises: true, unwrap: (delivery, _) => Trigger(delivery));
        var second = new StubEnvelope(recognises: true, unwrap: (delivery, _) => Trigger(delivery));

        await new TriggerFrontDoor([declines, first, second]).Execute(harness.Chain);

        Assert.Equal(1, declines.Unwrapped);
        Assert.Equal(1, first.Unwrapped);
        Assert.Equal(0, second.Unwrapped);
    }

    /// <summary>The envelopes read one buffered body, not one each.</summary>
    [Fact]
    public async Task TheBodyIsBufferedOnceForEveryEnvelope() {
        var harness = new Harness("POST", "application/json", "{\"a\":1}");
        TriggerPayload? seenByFirst = null;
        TriggerPayload? seenBySecond = null;

        var first = new StubEnvelope(recognises: true, unwrap: (_, payload) => { seenByFirst = payload; return null; });
        var second = new StubEnvelope(recognises: true, unwrap: (_, payload) => { seenBySecond = payload; return null; });

        await new TriggerFrontDoor([first, second]).Execute(harness.Chain);

        Assert.NotNull(seenByFirst);
        Assert.Same(seenByFirst, seenBySecond);
    }

    [Fact]
    public async Task ATriggerRequestIsNotUnwrappedAgain() {
        var harness = new Harness("POST", "application/json", "{}");
        var envelope = new StubEnvelope(recognises: true, unwrap: (delivery, _) => Trigger(delivery));
        var alreadyUnwrapped = Trigger(harness.Request);

        harness.Context.Request.Returns(alreadyUnwrapped);

        await new TriggerFrontDoor([envelope]).Execute(harness.Chain);

        await harness.Chain.Received(1).Next();
        Assert.Equal(0, envelope.Unwrapped);
    }

    [Fact]
    public async Task ADeclaredLengthOverTheLimitIsNotBuffered() {
        var harness = new Harness("POST", "application/json", "{}");
        var envelope = new StubEnvelope(recognises: true);

        harness.Request.Headers["Content-Length"] = (TriggerFrontDoor.BufferLimit + 1L).ToString();

        await new TriggerFrontDoor([envelope]).Execute(harness.Chain);

        await harness.Chain.Received(1).Next();
        Assert.Equal(0, envelope.Unwrapped);
    }

    /// <summary>An undeclared body that turns out too large has been consumed, so 413 is the only honest answer.</summary>
    [Fact]
    public async Task AnUndeclaredBodyOverTheLimitIsAnsweredTooLarge() {
        var harness = new Harness("POST", "application/json", body: new ZeroStream(TriggerFrontDoor.BufferLimit + 1L));
        var envelope = new StubEnvelope(recognises: true);

        await new TriggerFrontDoor([envelope]).Execute(harness.Chain);

        await harness.Chain.DidNotReceive().Next();
        Assert.Equal(413, harness.Response.Status);
        Assert.False(harness.Response.ShouldSerialize);
        Assert.Equal(0, envelope.Unwrapped);
    }

    private static CloudRunTriggerRequest Trigger(IExecutionRequest delivery) =>
        new("QUEUE", "/orders", Stream.Null, new Dictionary<string, StringValues>(), delivery);

    private sealed class StubEnvelope : ITriggerEnvelope {
        private readonly bool _recognises;
        private readonly Func<IExecutionRequest, TriggerPayload, CloudRunTriggerRequest?>? _unwrap;

        public StubEnvelope(bool recognises, Func<IExecutionRequest, TriggerPayload, CloudRunTriggerRequest?>? unwrap = null) {
            _recognises = recognises;
            _unwrap = unwrap;
        }

        public int Unwrapped { get; private set; }

        public bool Recognises(IExecutionRequest request) => _recognises;

        public CloudRunTriggerRequest? Unwrap(IExecutionRequest request, TriggerPayload payload) {
            Unwrapped++;

            return _unwrap?.Invoke(request, payload);
        }
    }

    /// <summary>A real request and response on a substituted chain, so the fork can be observed.</summary>
    private sealed class Harness {
        public Harness(string method, string contentType, string body)
            : this(method, contentType, new MemoryStream(Encoding.UTF8.GetBytes(body))) {
        }

        public Harness(string method, string contentType, Stream body) {
            Request = new TestExecutionRequest(method, "/", null, EmptyQueryStringCollection.Instance) {
                Headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase) {
                    ["Content-Type"] = contentType
                },
                Body = body
            };

            Response = new TestExecutionResponse(new MemoryStream());

            Context = Substitute.For<IExecutionContext>();
            Context.Request.Returns(Request);
            Context.Response.Returns(Response);
            Context.CancellationToken.Returns(CancellationToken.None);

            Forked = Substitute.For<IExecutionChain>();

            Context.Clone(Arg.Any<IExecutionRequest?>(), Arg.Any<IExecutionResponse?>(), Arg.Any<IServiceProvider?>(), Arg.Any<Hardened.Shared.Runtime.Metrics.IMetricLogger?>())
                .Returns(call => {
                    var clone = Substitute.For<IExecutionContext>();
                    clone.Request.Returns(call.ArgAt<IExecutionRequest?>(0) ?? Request);
                    clone.Response.Returns(Response);
                    ForkedContext = clone;
                    return clone;
                });

            Chain = Substitute.For<IExecutionChain>();
            Chain.Context.Returns(Context);
            Chain.Fork(Arg.Any<IExecutionContext>()).Returns(Forked);
        }

        public TestExecutionRequest Request { get; }

        public TestExecutionResponse Response { get; }

        public IExecutionContext Context { get; }

        public IExecutionChain Chain { get; }

        public IExecutionChain Forked { get; }

        public IExecutionContext? ForkedContext { get; private set; }
    }

    /// <summary>A body of zeros of any length, without the memory.</summary>
    private sealed class ZeroStream : Stream {
        private readonly long _length;
        private long _position;

        public ZeroStream(long length) {
            _length = length;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) {
            var remaining = (int)Math.Min(count, _length - _position);

            Array.Clear(buffer, offset, remaining);
            _position += remaining;

            return remaining;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
