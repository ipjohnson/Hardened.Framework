using DependencyModules.Testing.Attributes;
using Hardened.IntegrationTests.Kinesis.SUT;
using Hardened.Shared.Testing.Attributes;
using NSubstitute;
using Xunit;

namespace Hardened.IntegrationTests.Kinesis.SUT.Tests;

/// <summary>
/// A stream function, whole: the trigger attribute, the generator, the module the build property
/// named, the adapter, the base64 decode, the batch filter and the invocation loop.
/// </summary>
public class StreamTests {

    /// <summary>
    /// The claim the adapter rests on: a handler that names a stream and binds a plain type is
    /// reached with the record the publisher wrote.
    /// </summary>
    [HardenedTest]
    public async Task ARecordReachesTheHandler(
        StreamTestApp.Streams streams, [Mock] IClickSink sink) {
        await streams.Clickstream(new Click { Id = "c-1", Count = 3 });

        sink.Received().Record(Arg.Is<Click>(click => click.Id == "c-1" && click.Count == 3));
    }

    /// <summary>
    /// One invocation, one handler call per record, in the order the shard delivered them.
    /// </summary>
    [HardenedTest]
    public async Task EveryRecordInABatchIsHandledSeparately(
        StreamTestApp.Streams streams, [Mock] IClickSink sink) {
        await streams.Clickstream(
            new Click { Id = "c-1" }, new Click { Id = "c-2" }, new Click { Id = "c-3" });

        sink.Received(3).Record(Arg.Any<Click>());
        sink.Received().Record(Arg.Is<Click>(click => click.Id == "c-2"));
    }

    /// <summary>
    /// Each fork binds its own record's data rather than the batch it arrived in.
    /// </summary>
    [HardenedTest]
    public async Task EachRecordBindsItsOwnData(
        StreamTestApp.Streams streams, [Mock] IClickSink sink) {
        await streams.Clickstream(
            new Click { Id = "c-1", Count = 10 }, new Click { Id = "c-2", Count = 20 });

        sink.Received().Record(Arg.Is<Click>(c => c.Id == "c-1" && c.Count == 10));
        sink.Received().Record(Arg.Is<Click>(c => c.Id == "c-2" && c.Count == 20));
    }

    /// <summary>
    /// Failing the invocation is what replays the shard. With ReportBatchItemFailures off - the
    /// default - a failed record has to take the whole batch with it.
    /// </summary>
    [HardenedTest]
    public async Task AFailedRecordFailsTheInvocation(
        StreamTestApp.Streams streams, [Mock] IClickSink sink) {
        sink.When(one => one.Record(Arg.Is<Click>(click => click.Id == "c-2")))
            .Do(_ => throw new InvalidOperationException("refused"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => streams.Clickstream(new Click { Id = "c-1" }, new Click { Id = "c-2" }));
    }
}
