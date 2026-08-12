using Amazon.Lambda.DynamoDBEvents;
using Hardened.Amz.Function.DDB.Runtime.Attributes;
using Hardened.Amz.Function.DDB.Runtime.Impl;
using Hardened.Amz.Function.DDB.Runtime.Tests.Infrastructure;
using Hardened.Requests.Abstract.Execution;
using static Hardened.Amz.Function.DDB.Runtime.Tests.Infrastructure.DdbStreamHarness;

namespace Hardened.Amz.Function.DDB.Runtime.Tests.Attributes;

/// <summary>
/// <c>[NewImage]</c> and <c>[OldImage]</c> bind a handler parameter to one of the two item images a
/// DynamoDB stream record carries. They read the record out of the request scope rather than the
/// request body, because the batch filter never writes a stream record into a request body.
/// </summary>
public class ImageBindingTests {

    private static IExecutionContext ContextFor(DynamoDBEvent.DynamodbStreamRecord record) {
        var recordContext = new CurrentDdbRecordContext { CurrentRecord = record };

        return TestExecutionContext.Create(
            new MemoryStream(), new MemoryStream(), new StubServiceProvider().Add(recordContext));
    }

    [Fact]
    public async Task NewImageBindsTheImageTheRecordCarries() {
        var newImage = Image(("id", "1"), ("status", "shipped"));
        var context = ContextFor(Record("e1", Modify, newImage: newImage, oldImage: Image(("status", "pending"))));

        var bound = await new NewImageAttribute()
            .BindValue<Dictionary<string, DynamoDBEvent.AttributeValue>>(context, null!);

        Assert.Same(newImage, bound);
        Assert.Equal("shipped", bound["status"].S);
    }

    [Fact]
    public async Task OldImageBindsTheImageTheRecordCarried() {
        var oldImage = Image(("id", "1"), ("status", "pending"));
        var context = ContextFor(Record("e1", Modify, newImage: Image(("status", "shipped")), oldImage: oldImage));

        var bound = await new OldImageAttribute()
            .BindValue<Dictionary<string, DynamoDBEvent.AttributeValue>>(context, null!);

        Assert.Same(oldImage, bound);
        Assert.Equal("pending", bound["status"].S);
    }

    /// <summary>
    /// The two attributes bind different images. Wiring both to the same one is invisible to a test
    /// that only checks a value came back.
    /// </summary>
    [Fact]
    public async Task TheTwoAttributesBindDifferentImagesOfTheSameRecord() {
        var newImage = Image(("status", "shipped"));
        var oldImage = Image(("status", "pending"));
        var context = ContextFor(Record("e1", Modify, newImage: newImage, oldImage: oldImage));

        var boundNew = await new NewImageAttribute()
            .BindValue<Dictionary<string, DynamoDBEvent.AttributeValue>>(context, null!);
        var boundOld = await new OldImageAttribute()
            .BindValue<Dictionary<string, DynamoDBEvent.AttributeValue>>(context, null!);

        Assert.Same(newImage, boundNew);
        Assert.Same(oldImage, boundOld);
    }

    /// <summary>
    /// An image is a <c>Dictionary&lt;string, AttributeValue&gt;</c> and nothing else. The
    /// attributes cannot deserialise into a model type, so a parameter declared as one has to fail
    /// loudly rather than bind null.
    /// </summary>
    [Fact]
    public async Task NewImageBoundToAnythingButTheAttributeDictionaryThrows() {
        var context = ContextFor(Record("e1", Modify, newImage: Image(("id", "1"))));

        var exception = await Assert.ThrowsAsync<InvalidCastException>(
            async () => await new NewImageAttribute().BindValue<string>(context, null!));

        Assert.Contains("NewImage", exception.Message);
    }

    [Fact]
    public async Task OldImageBoundToAnythingButTheAttributeDictionaryThrows() {
        var context = ContextFor(Record("e1", Modify, oldImage: Image(("id", "1"))));

        var exception = await Assert.ThrowsAsync<InvalidCastException>(
            async () => await new OldImageAttribute().BindValue<string>(context, null!));

        Assert.Contains("OldImage", exception.Message);
    }

    /// <summary>
    /// An INSERT has no old image and a REMOVE has no new image. The missing side is null, and the
    /// type test on null fails, so the binding throws <see cref="InvalidCastException"/> rather
    /// than handing the handler a null dictionary.
    ///
    /// <para>
    /// Worth knowing before writing a handler: a single handler taking both
    /// <c>[NewImage]</c> and <c>[OldImage]</c> can only process MODIFY records. Under INSERT or
    /// REMOVE it throws, the record is named in the batch response, and the shard retries it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task OldImageOnAnInsertThrowsBecauseThereIsNoPreviousItem() {
        var context = ContextFor(Record("e1", Insert, newImage: Image(("id", "1"))));

        await Assert.ThrowsAsync<InvalidCastException>(
            async () => await new OldImageAttribute()
                .BindValue<Dictionary<string, DynamoDBEvent.AttributeValue>>(context, null!));
    }

    [Fact]
    public async Task NewImageOnARemoveThrowsBecauseThereIsNoRemainingItem() {
        var context = ContextFor(Record("e1", Remove, oldImage: Image(("id", "1"))));

        await Assert.ThrowsAsync<InvalidCastException>(
            async () => await new NewImageAttribute()
                .BindValue<Dictionary<string, DynamoDBEvent.AttributeValue>>(context, null!));
    }

    /// <summary>
    /// The record is read from the request scope, not the root provider. A binding that reached the
    /// root would see whichever record the previous invocation left behind.
    /// </summary>
    [Fact]
    public async Task TheRecordIsResolvedFromTheRequestScope() {
        var context = ContextFor(Record("e1", Modify, newImage: Image(("id", "1"))));

        await new NewImageAttribute()
            .BindValue<Dictionary<string, DynamoDBEvent.AttributeValue>>(context, null!);

        Assert.NotNull(context.RequestServices.GetService(typeof(CurrentDdbRecordContext)));
    }

    /// <summary>
    /// Nothing registered the record context: a handler using the binding outside a DynamoDB stream
    /// invocation gets a resolution failure naming the missing service, not a null reference deeper
    /// in the binding.
    /// </summary>
    [Fact]
    public async Task BindingWithNoRecordContextRegisteredFailsAtResolution() {
        var context = TestExecutionContext.Create(
            new MemoryStream(), new MemoryStream(), new StubServiceProvider());

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await new NewImageAttribute()
                .BindValue<Dictionary<string, DynamoDBEvent.AttributeValue>>(context, null!));
    }
}
