using DependencyModules.Testing.Attributes;
using Hardened.IntegrationTests.AzureStream.SUT;
using Hardened.Shared.Testing.Attributes;
using NSubstitute;
using Xunit;

namespace Hardened.IntegrationTests.AzureStream.SUT.Tests;

/// <summary>
/// A stream function, whole: the trigger attribute, the generators, the module the build property
/// named, the adapter, the batch filter and the invocation handler.
///
/// <para>
/// The Kinesis fixture's test file unchanged, on purpose, for the reason the queue fixture gives.
/// It is compiled into two test projects - this one, which delivers through the pipeline, and the
/// worker rung, which delivers the events the isolated worker would bind - and passes in both.
/// </para>
/// </summary>
public class StreamTests {

    /// <summary>
    /// The claim the adapter rests on: a handler that names a stream and binds a plain type is
    /// reached with the event the publisher wrote.
    /// </summary>
    [HardenedTest]
    public async Task ARecordReachesTheHandler(
        AzureStreamTestApp.Streams streams, [Mock] IClickSink sink) {
        await streams.Clickstream(new Click { Id = "c-1", Count = 3 });

        sink.Received().Record(Arg.Is<Click>(click => click.Id == "c-1" && click.Count == 3));
    }

    /// <summary>
    /// One invocation, one handler call per event, in the order the partition delivered them.
    /// </summary>
    [HardenedTest]
    public async Task EveryRecordInABatchIsHandledSeparately(
        AzureStreamTestApp.Streams streams, [Mock] IClickSink sink) {
        await streams.Clickstream(
            new Click { Id = "c-1" }, new Click { Id = "c-2" }, new Click { Id = "c-3" });

        sink.Received(3).Record(Arg.Any<Click>());
        sink.Received().Record(Arg.Is<Click>(click => click.Id == "c-2"));
    }

    /// <summary>
    /// Each fork binds its own event's body rather than the batch it arrived in.
    /// </summary>
    [HardenedTest]
    public async Task EachRecordBindsItsOwnData(
        AzureStreamTestApp.Streams streams, [Mock] IClickSink sink) {
        await streams.Clickstream(
            new Click { Id = "c-1", Count = 10 }, new Click { Id = "c-2", Count = 20 });

        sink.Received().Record(Arg.Is<Click>(c => c.Id == "c-1" && c.Count == 10));
        sink.Received().Record(Arg.Is<Click>(c => c.Id == "c-2" && c.Count == 20));
    }

    /// <summary>
    /// Failing the invocation is what keeps the checkpoint where it was, so the partition replays.
    /// Nothing reports individual events, so a failed event has to take the whole batch with it.
    /// </summary>
    [HardenedTest]
    public async Task AFailedRecordFailsTheInvocation(
        AzureStreamTestApp.Streams streams, [Mock] IClickSink sink) {
        sink.When(one => one.Record(Arg.Is<Click>(click => click.Id == "c-2")))
            .Do(_ => throw new InvalidOperationException("refused"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => streams.Clickstream(new Click { Id = "c-1" }, new Click { Id = "c-2" }));
    }
}
