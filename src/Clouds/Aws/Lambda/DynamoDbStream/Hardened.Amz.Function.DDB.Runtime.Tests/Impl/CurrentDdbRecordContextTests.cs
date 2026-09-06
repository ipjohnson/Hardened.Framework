using Amazon.Lambda.DynamoDBEvents;
using Hardened.Amz.Function.DDB.Runtime.Impl;
using Hardened.Amz.Function.DDB.Runtime.Tests.Infrastructure;
using static Hardened.Amz.Function.DDB.Runtime.Tests.Infrastructure.DdbStreamHarness;

namespace Hardened.Amz.Function.DDB.Runtime.Tests.Impl;

/// <summary>
/// The record a handler is currently processing. A stream record never becomes a request body, so
/// this is the only route from the batch filter to the <c>[NewImage]</c> / <c>[OldImage]</c>
/// bindings — and it is mutated in place, once per record, before each forked chain runs.
/// </summary>
public class CurrentDdbRecordContextTests {

    [Fact]
    public async Task EachRecordIsPublishedBeforeItsOwnHandlerRuns() {
        var seen = new List<string>();

        await Run(
            [Record("first", Insert), Record("second", Modify), Record("third", Remove)],
            (_, recordContext) => {
                seen.Add(recordContext.CurrentRecord.EventID);

                return Task.CompletedTask;
            });

        Assert.Equal(new[] { "first", "second", "third" }, seen);
    }

    /// <summary>
    /// The images a handler binds come off whichever record is current. Publishing the record after
    /// the fork, or only once for the batch, would hand every handler the same item.
    /// </summary>
    [Fact]
    public async Task TheImagesVisibleToAHandlerBelongToTheRecordBeingProcessed() {
        var seen = new List<string>();

        await Run(
            [
                Record("first", Modify, newImage: Image(("status", "one"))),
                Record("second", Modify, newImage: Image(("status", "two")))
            ],
            (_, recordContext) => {
                seen.Add(recordContext.CurrentRecord.Dynamodb.NewImage["status"].S);

                return Task.CompletedTask;
            });

        Assert.Equal(new[] { "one", "two" }, seen);
    }

    /// <summary>
    /// A record that threw still leaves the next record published correctly — the failure path must
    /// not strand the context on the record that blew up.
    /// </summary>
    [Fact]
    public async Task ARecordThatThrewDoesNotStrandTheContextForTheNextRecord() {
        var seen = new List<string>();

        await Run(
            [Record("first", Insert), Record("second", Modify)],
            (_, recordContext) => {
                seen.Add(recordContext.CurrentRecord.EventID);

                if (recordContext.CurrentRecord.EventID == "first") {
                    throw new InvalidOperationException("handler failed");
                }

                return Task.CompletedTask;
            });

        Assert.Equal(new[] { "first", "second" }, seen);
    }

    [Fact]
    public void TheContextIsAPlainMutableHolder() {
        var record = new DynamoDBEvent.DynamodbStreamRecord { EventID = "e1" };

        var context = new CurrentDdbRecordContext { CurrentRecord = record };

        Assert.Same(record, context.CurrentRecord);
    }
}
