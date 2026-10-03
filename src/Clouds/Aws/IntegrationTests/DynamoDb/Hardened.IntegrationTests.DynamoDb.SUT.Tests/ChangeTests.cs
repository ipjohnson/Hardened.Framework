using Amazon.Lambda.DynamoDBEvents;
using DependencyModules.Testing.Attributes;
using DependencyModules.xUnit.Attributes;
using Hardened.IntegrationTests.DynamoDb.SUT;
using Hardened.Requests.Abstract.Execution;
using NSubstitute;
using Xunit;

namespace Hardened.IntegrationTests.DynamoDb.SUT.Tests;

/// <summary>
/// A change feed function, whole: the trigger attribute, the generator, the module the build
/// property named, the adapter, the unmarshalling, the batch filter and the invocation loop.
///
/// <para>
/// The delivery marshals each message into DynamoDB's type-tagged wire form before it goes in, so
/// what the adapter reads is what AWS sends rather than the shape the adapter would have produced.
/// Without that this suite would assert that a round trip through nothing preserves a value.
/// </para>
/// </summary>
public class ChangeTests
{
    /// <summary>
    /// The claim the adapter rests on: a handler that names a table and binds a plain type is
    /// reached with the row, and never sees an AttributeValue.
    /// </summary>
    [ModuleTest]
    public async Task AChangeReachesTheHandlerAsThePlainRow(
        ChangeTestApp.Changes changes,
        [Mock] IOrderProjection projection
    )
    {
        await changes.Orders(
            new Order
            {
                Id = "a-1",
                Quantity = 2,
                Total = 42.5m,
            }
        );

        projection
            .Received()
            .Apply(
                Arg.Is<Order>(order =>
                    order.Id == "a-1" && order.Quantity == 2 && order.Total == 42.5m
                )
            );
    }

    /// <summary>
    /// One invocation, one handler call per change. The route was chosen once from the table the
    /// batch arrived against; the fan-out is the filter.
    /// </summary>
    [ModuleTest]
    public async Task EveryChangeInABatchIsHandledSeparately(
        ChangeTestApp.Changes changes,
        [Mock] IOrderProjection projection
    )
    {
        await changes.Orders(
            new Order { Id = "a-1" },
            new Order { Id = "a-2" },
            new Order { Id = "a-3" }
        );

        projection.Received(3).Apply(Arg.Any<Order>());
        projection.Received().Apply(Arg.Is<Order>(order => order.Id == "a-2"));
    }

    /// <summary>
    /// Each fork binds its own record's image, so a handler sees the row that changed rather than
    /// the batch it arrived in.
    /// </summary>
    [ModuleTest]
    public async Task EachChangeBindsItsOwnImage(
        ChangeTestApp.Changes changes,
        [Mock] IOrderProjection projection
    )
    {
        await changes.Orders(
            new Order { Id = "a-1", Quantity = 10 },
            new Order { Id = "a-2", Quantity = 20 }
        );

        projection.Received().Apply(Arg.Is<Order>(o => o.Id == "a-1" && o.Quantity == 10));
        projection.Received().Apply(Arg.Is<Order>(o => o.Id == "a-2" && o.Quantity == 20));
    }

    /// <summary>
    /// <c>[NewImage]</c> reaches the row in DynamoDB's own form, with the type tags still on.
    /// </summary>
    /// <remarks>
    /// The half of the record the bound parameter cannot express. It arrives off the request rather
    /// than off a singleton holding "the record being handled", which is what Hardened.Amz did.
    /// </remarks>
    [ModuleTest]
    public async Task TheRawImageIsReachableThroughNewImage(
        ChangeTestApp.Changes changes,
        [Mock] IOrderProjection projection
    )
    {
        await changes.Audit(new Order { Id = "a-9", Total = 7 });

        projection
            .Received()
            .Raw(
                Arg.Is<
                    IDictionary<string, Amazon.Lambda.DynamoDBEvents.DynamoDBEvent.AttributeValue>
                >(image => image["id"].S == "a-9" && image["total"].N == "7")
            );
    }

    /// <summary>
    /// A transition sends a MODIFY whose images differ, so a handler comparing them can be tested
    /// through the façade.
    /// </summary>
    [ModuleTest]
    public async Task AModifyCarriesTheRowBeforeAndTheRowAfter(
        ChangeTestApp.Changes changes,
        [Mock] IOrderProjection projection
    )
    {
        await changes.History(
            Transition.Modify(
                new Order { Id = "h-1", Quantity = 1 },
                new Order { Id = "h-1", Quantity = 5 }
            )
        );

        projection.Received().Apply(Arg.Is<Order>(order => order.Quantity == 5));
        projection
            .Received()
            .Transition(
                Arg.Is<string?>("MODIFY"),
                Arg.Is<IDictionary<string, DynamoDBEvent.AttributeValue>>(image =>
                    image["quantity"].N == "1"
                ),
                Arg.Is<IDictionary<string, DynamoDBEvent.AttributeValue>>(image =>
                    image["quantity"].N == "5"
                )
            );
    }

    [ModuleTest]
    public async Task AnInsertHasNoRowBefore(
        ChangeTestApp.Changes changes,
        [Mock] IOrderProjection projection
    )
    {
        await changes.History(Transition.Insert(new Order { Id = "h-2", Quantity = 3 }));

        projection
            .Received()
            .Transition(
                Arg.Is<string?>("INSERT"),
                Arg.Is<IDictionary<string, DynamoDBEvent.AttributeValue>?>(image => image == null),
                Arg.Is<IDictionary<string, DynamoDBEvent.AttributeValue>>(image =>
                    image["id"].S == "h-2"
                )
            );
    }

    /// <summary>
    /// A REMOVE has no new image, so the handler binds the row as it was.
    /// </summary>
    [ModuleTest]
    public async Task ARemoveBindsTheRowBefore(
        ChangeTestApp.Changes changes,
        [Mock] IOrderProjection projection
    )
    {
        await changes.History(Transition.Remove(new Order { Id = "h-3", Quantity = 4 }));

        projection
            .Received()
            .Apply(Arg.Is<Order>(order => order.Id == "h-3" && order.Quantity == 4));
        projection
            .Received()
            .Transition(
                Arg.Is<string?>("REMOVE"),
                Arg.Is<IDictionary<string, DynamoDBEvent.AttributeValue>>(image =>
                    image["id"].S == "h-3"
                ),
                Arg.Is<IDictionary<string, DynamoDBEvent.AttributeValue>?>(image => image == null)
            );
    }

    /// <summary>
    /// A plain message is still a MODIFY with the message on both sides.
    /// </summary>
    [ModuleTest]
    public async Task APlainMessageIsAModifyOfTheSameRow(
        ChangeTestApp.Changes changes,
        [Mock] IOrderProjection projection
    )
    {
        await changes.History(new Order { Id = "h-4", Quantity = 6 });

        projection
            .Received()
            .Transition(
                Arg.Is<string?>("MODIFY"),
                Arg.Is<IDictionary<string, DynamoDBEvent.AttributeValue>>(image =>
                    image["quantity"].N == "6"
                ),
                Arg.Is<IDictionary<string, DynamoDBEvent.AttributeValue>>(image =>
                    image["quantity"].N == "6"
                )
            );
    }

    /// <summary>
    /// Failing the invocation is what replays the shard. With ReportBatchItemFailures off - the
    /// default, because a report sent to a mapping that did not ask for one is discarded - a failed
    /// change has to take the whole batch with it.
    /// </summary>
    [ModuleTest]
    public async Task AFailedChangeFailsTheInvocation(
        ChangeTestApp.Changes changes,
        [Mock] IOrderProjection projection
    )
    {
        projection
            .When(one => one.Apply(Arg.Is<Order>(order => order.Id == "a-2")))
            .Do(_ => throw new InvalidOperationException("refused"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            changes.Orders(new Order { Id = "a-1" }, new Order { Id = "a-2" })
        );
    }
}
