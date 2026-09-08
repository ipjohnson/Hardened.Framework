using System.Text.Json;
using Google.Events.Protobuf.Cloud.Firestore.V1;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Hardened.Gcp.CloudRun.Firestore;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Testing;
using NSubstitute;
using Xunit;
using Value = Google.Events.Protobuf.Cloud.Firestore.V1.Value;

namespace Hardened.Gcp.CloudRun.Runtime.Tests.Envelopes;

/// <summary>
/// A document event delivered by Eventarc as protobuf, read into the plain JSON a handler binds.
/// </summary>
public class FirestoreEnvelopeTests {
    private static readonly FirestoreEnvelope Envelope = new();

    private const string Database = "//firestore.googleapis.com/projects/p/databases/(default)";

    private static Document Order(string id, long quantity) =>
        new() {
            Name = "projects/p/databases/(default)/documents/orders/" + id,
            Fields = {
                ["id"] = new Value { StringValue = id },
                ["quantity"] = new Value { IntegerValue = quantity },
                ["total"] = new Value { DoubleValue = 42.5 },
                ["paid"] = new Value { BooleanValue = true },
                ["placed"] = new Value { TimestampValue = Timestamp.FromDateTimeOffset(new DateTimeOffset(2026, 9, 7, 10, 0, 0, TimeSpan.Zero)) },
                ["tags"] = new Value { ArrayValue = new ArrayValue { Values = { new Value { StringValue = "a" }, new Value { IntegerValue = 2 } } } },
                ["address"] = new Value { MapValue = new MapValue { Fields = { ["city"] = new Value { StringValue = "Leeds" } } } },
                ["note"] = new Value { NullValue = NullValue.NullValue }
            }
        };

    private static byte[] Event(Document? value, Document? oldValue) =>
        new DocumentEventData { Value = value, OldValue = oldValue }.ToByteArray();

    private static JsonElement Body(Stream body) {
        using var document = JsonDocument.Parse(Deliveries.Text(body));

        return document.RootElement.Clone();
    }

    private static FirestoreChange Unwrap(string type, string subject, byte[] data) =>
        (FirestoreChange)Deliveries.Unwrap(
            Envelope,
            Deliveries.CloudEvent(FirestoreEnvelope.DocumentTypePrefix + type, Database, subject, "application/protobuf"),
            data)!;

    [Fact]
    public void TheCollectionOffTheSubjectIsTheRoute() {
        var request = Unwrap("updated", "documents/orders/o-1", Event(Order("o-1", 2), Order("o-1", 1)));

        Assert.Equal("CHANGE", request.Method);
        Assert.Equal("/orders", request.Path);
    }

    /// <summary>The typed values become the plain JSON a handler's own type is shaped like.</summary>
    [Fact]
    public void TheDocumentsFieldsAreTheBodyAsPlainJson() {
        var request = Unwrap("updated", "documents/orders/o-1", Event(Order("o-1", 2), null));

        var body = Body(request.Body);

        Assert.Equal("o-1", body.GetProperty("id").GetString());
        Assert.Equal(2, body.GetProperty("quantity").GetInt64());
        Assert.Equal(42.5, body.GetProperty("total").GetDouble());
        Assert.True(body.GetProperty("paid").GetBoolean());
        Assert.StartsWith("2026-09-07T10:00:00", body.GetProperty("placed").GetString());
        Assert.Equal(2, body.GetProperty("tags").GetArrayLength());
        Assert.Equal("Leeds", body.GetProperty("address").GetProperty("city").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("note").ValueKind);
    }

    /// <summary>A delete binds the old value, because there is no new one.</summary>
    [Fact]
    public void ADeleteBindsTheDocumentThatWasDeleted() {
        var request = Unwrap("deleted", "documents/orders/o-9", Event(null, Order("o-9", 5)));

        Assert.Equal("o-9", Body(request.Body).GetProperty("id").GetString());
        Assert.Null(request.Value);
        Assert.NotNull(request.OldValue);
    }

    [Fact]
    public void ASubcollectionDocumentRoutesOnItsOwnCollection() {
        var request = Unwrap("created", "documents/users/u-1/orders/o-1", Event(Order("o-1", 1), null));

        Assert.Equal("/orders", request.Path);
        Assert.Equal("users/u-1/orders/o-1", request.Headers[FirestoreEnvelope.DocumentHeader].ToString());
    }

    [Fact]
    public void TheEventAndTheDocumentNameAreHeaders() {
        var request = Unwrap("created", "documents/orders/o-1", Event(Order("o-1", 1), null));

        Assert.Equal(FirestoreEnvelope.DocumentTypePrefix + "created", request.Headers["ce-type"].ToString());
        Assert.Equal("projects/p/databases/(default)/documents/orders/o-1", request.Headers[FirestoreEnvelope.DocumentNameHeader].ToString());
    }

    /// <summary>A fork keeps the event, so a filter that re-runs the handler still serves [OldValue].</summary>
    [Fact]
    public void ACloneKeepsTheEvent() {
        var request = Unwrap("updated", "documents/orders/o-1", Event(Order("o-1", 2), Order("o-1", 1)));

        var clone = Assert.IsType<FirestoreChange>(request.Clone(method: "CHANGE"));

        Assert.Same(request.Event, clone.Event);
    }

    [Fact]
    public void DataThatIsNotADocumentEventIsRefused() {
        Assert.Throws<InvalidOperationException>(() => Unwrap("updated", "documents/orders/o-1", new byte[] { 0xff, 0xff, 0xff }));
    }

    [Fact]
    public void ACloudEventOfAnotherTypeIsNotRecognised() {
        Assert.False(Envelope.Recognises(Deliveries.CloudEvent("google.cloud.storage.object.v1.finalized", "/s")));
    }

    [Theory]
    [InlineData("orders/o-1", "orders")]
    [InlineData("users/u-1/orders/o-1", "orders")]
    [InlineData("orders", "orders")]
    [InlineData("", "")]
    public void TheCollectionIsTheSegmentBeforeTheDocument(string path, string collection) {
        Assert.Equal(collection, FirestoreEnvelope.Collection(path));
    }

    [Fact]
    public async Task OldValueBindsThePreviousDocumentInFirestoresOwnForm() {
        var request = Unwrap("updated", "documents/orders/o-1", Event(Order("o-1", 2), Order("o-1", 1)));
        var context = Context(request);

        var previous = await new OldValueAttribute().BindValue<Document>(context, Parameter());

        Assert.Equal(1, previous.Fields["quantity"].IntegerValue);
    }

    [Fact]
    public async Task OldValueIsNullOnACreateForAParameterThatCanHoldNull() {
        var request = Unwrap("created", "documents/orders/o-1", Event(Order("o-1", 2), null));

        Assert.Null(await new OldValueAttribute().BindValue<Document?>(Context(request), Parameter()));
    }

    [Fact]
    public async Task OldValueRefusesAParameterThatCannotHoldNullOnACreate() {
        var request = Unwrap("created", "documents/orders/o-1", Event(Order("o-1", 2), null));

        await Assert.ThrowsAsync<InvalidCastException>(
            () => new OldValueAttribute().BindValue<int>(Context(request), Parameter()).AsTask());
    }

    [Fact]
    public async Task OldValueRefusesARequestThatIsNotAFirestoreChange() {
        var context = Substitute.For<IExecutionContext>();

        context.Request.Returns(new TestExecutionRequest("CHANGE", "/orders", null, Hardened.Requests.Runtime.QueryString.EmptyQueryStringCollection.Instance));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new OldValueAttribute().BindValue<Document>(context, Parameter()).AsTask());
    }

    private static IExecutionContext Context(FirestoreChange request) {
        var context = Substitute.For<IExecutionContext>();

        context.Request.Returns(request);

        return context;
    }

    private static IExecutionRequestParameter Parameter() {
        var parameter = Substitute.For<IExecutionRequestParameter>();

        parameter.Name.Returns("previous");

        return parameter;
    }
}
