using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Runtime.Serializer;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Serializer;

/// <summary>
/// The rule that makes a hand-written model's <c>required</c> array mean something.
/// </summary>
/// <remarks>
/// <para>
/// <c>record NewTodo(string Title)</c> published <c>required: ["title"]</c> from its nullable
/// annotation and answered 201 to <c>{}</c>: the array is written from nullability and the validator
/// was built from <c>[Required]</c> alone, so the two halves of one declaration never met. These pin
/// the half that closes it, member kind by member kind - and the exclusions, which are what keep it
/// from demanding values no caller was ever meant to send.
/// </para>
/// <para>
/// Driven through <c>JsonSerializer</c> rather than by inspecting <c>JsonTypeInfo</c>, because what
/// is under test is whether a body is refused.
/// </para>
/// </remarks>
public class RequiredMemberPresenceTests {

    private static JsonSerializerOptions Options() =>
        RequiredMemberPresence.Enforce(
            new JsonSerializerOptions(JsonSerializerDefaults.Web) {
                TypeInfoResolver = new DefaultJsonTypeInfoResolver()
            });

    private static JsonException Refused<T>(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<T>(json, Options()));

    private static T Accepted<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options())!;

    // ---------------------------------------------------------------- what is required

    private record Todo(string Title);

    [Fact]
    public void ANonNullableReferenceMemberMustBeSent() {
        Assert.Contains("title", Refused<Todo>("{}").Message);
    }

    /// <summary>
    /// Every one of them, in a single answer. The reader aggregates, so a caller fixes their request
    /// in one pass rather than one round trip per member.
    /// </summary>
    private record Quote(string AccountId, string Origin, string Destination);

    [Fact]
    public void EveryMissingMemberIsNamedAtOnce() {
        var message = Refused<Quote>("""{"origin":"SEA"}""").Message;

        Assert.Contains("accountId", message);
        Assert.Contains("destination", message);
        Assert.DoesNotContain("origin", message);
    }

    /// <summary>
    /// Sent is sent. A <c>null</c> satisfies this rule and is <c>[Required]</c>'s to refuse - which
    /// is the split: presence is the reader's question, content is the validator's.
    /// </summary>
    [Fact]
    public void AnExplicitNullIsNotAbsence() {
        Assert.Null(Accepted<Todo>("""{"title":null}""").Title);
    }

    // ---------------------------------------------------------------- what is not

    private record Optional(string? Title);

    [Fact]
    public void ANullableReferenceMemberMayBeOmitted() {
        Assert.Null(Accepted<Optional>("{}").Title);
    }

    /// <summary>
    /// A value type is left alone. The document publishes it as required because C# serialization
    /// cannot omit one, which is a fact about what a response contains rather than a demand its
    /// author made - and <c>int?</c> or <c>= 0</c> are the only ways C# has to say otherwise, so
    /// reading it as a demand would make every declared <c>int</c> mandatory. See HRDV003.
    /// </summary>
    private record Weighed(int Grams, bool Fragile);

    [Fact]
    public void AValueTypeMemberIsLeftAlone() {
        Assert.Equal(0, Accepted<Weighed>("{}").Grams);
    }

    /// <summary>The server fills it in, so the caller may leave it out. The document agrees.</summary>
    private record Paged(string Cursor = "");

    [Fact]
    public void AMemberWithAConstructorDefaultMayBeOmitted() {
        Assert.Equal("", Accepted<Paged>("{}").Cursor);
    }

    /// <summary>
    /// <c>[ResponseOnly]</c> is OpenAPI's <c>readOnly</c>: the server owns the value. Demanding one
    /// would refuse the create call of a client that correctly left it out.
    /// </summary>
    private record Assigned([property: ResponseOnly] string Id, string Title);

    [Fact]
    public void AResponseOnlyMemberIsNotDemanded() {
        var accepted = Accepted<Assigned>("""{"title":"t"}""");

        Assert.Null(accepted.Id);
        Assert.Equal("t", accepted.Title);
    }

    /// <summary>
    /// A member the reader cannot fill. There is no constructor parameter for it and no setter, so
    /// requiring it would refuse every request for a value that could never arrive.
    /// </summary>
    private record Computed(string Title) {
        public string Slug => Title.ToLowerInvariant();
    }

    [Fact]
    public void AGetOnlyMemberWithNoConstructorParameterIsNotDemanded() {
        Assert.Equal("t", Accepted<Computed>("""{"title":"t"}""").Title);
    }

    /// <summary>
    /// An assembly compiled without nullable annotations declares nothing, and this reads a
    /// declaration. The document says the same: it writes <c>required</c> from the annotation, and
    /// there is none.
    /// </summary>
    [Fact]
    public void AMemberWithNoNullableAnnotationIsNotDemanded() {
        Assert.Null(Accepted<Unannotated>("{}").Title);
    }

    /// <summary>
    /// Already required, and nothing here undoes that: the rule adds, and <c>[JsonRequired]</c> or
    /// the <c>required</c> modifier is a declaration of its own.
    /// </summary>
    private record Explicit([property: JsonRequired] int Grams);

    [Fact]
    public void AMemberAlreadyRequiredStaysRequired() {
        Assert.Contains("grams", Refused<Explicit>("{}").Message);
    }
}

#nullable disable

/// <summary>
/// Declared outside the nullable context on purpose - see
/// <see cref="RequiredMemberPresenceTests.AMemberWithNoNullableAnnotationIsNotDemanded"/>.
/// </summary>
public record Unannotated(string Title);

#nullable restore
