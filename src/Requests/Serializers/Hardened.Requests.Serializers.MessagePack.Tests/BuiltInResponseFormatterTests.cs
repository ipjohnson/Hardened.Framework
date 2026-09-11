using System.Text.Json;
using Hardened.Requests.Abstract.Authorization;
using Hardened.Web.Runtime.Responses;
using MessagePack;
using MessagePack.Resolvers;
using Xunit;

namespace Hardened.Requests.Serializers.MessagePack.Tests;

/// <summary>
/// Hardened's own response bodies, as MessagePack.
/// </summary>
/// <remarks>
/// <para>
/// An operation declaring <c>application/x-msgpack</c> and returning
/// <c>Response&lt;Todo, NotFound&gt;</c> could not answer its 404: the writer was asked for a
/// <c>NotFound</c>, a type in <c>Hardened.Web.Runtime</c> that cannot carry
/// <c>[MessagePackObject]</c>, and found no formatter. So the media type could only be declared on
/// an operation with a single outcome.
/// </para>
/// <para>
/// <b>The invariant these hold is agreement with JSON</b>, not any particular byte layout. The two
/// representations describe one document, so a member the JSON body carries and the MessagePack
/// body does not is a client reading null for something the schema says is always there - and
/// nothing anywhere reports it.
/// </para>
/// </remarks>
public class BuiltInResponseFormatterTests {

    private static readonly MessagePackSerializerOptions Options =
        MessagePackSerializerOptions.Standard.WithResolver(
            CompositeResolver.Create([], [HardenedFormatterResolver.Instance, StandardResolver.Instance]));

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>Every built-in response type that reaches a serializer, with a populated body.</summary>
    /// <remarks>
    /// The generic wrappers are deliberately absent: the generated dispatch assigns
    /// <c>ICarriesResponseBody.Body</c>, so a <c>Created&lt;Todo&gt;</c> sends its <c>Todo</c> and
    /// never arrives at a formatter. So are the ones with <c>HasBody =&gt; false</c>, which write
    /// nothing at all. If that ever changes, this list is where the new arrival belongs.
    /// </remarks>
    public static IEnumerable<object[]> Bodies() => new[] {
        new object[] { new BadRequest("bad") },
        new object[] { new Unauthorized("who", AuthorizationChallenge.InvalidToken("realm", "expired")) },
        new object[] { new PaymentRequired("pay") },
        new object[] { new Forbidden("no") },
        new object[] { new NotFound("todo", "No todo has id 9.") },
        new object[] { new Conflict("taken") },
        new object[] { new Gone("gone") },
        new object[] { new PreconditionFailed("stale") },
        new object[] { new PreconditionRequired("need one") },
        new object[] { new ContentTooLarge("big") },
        new object[] { new UnsupportedMediaType("nope") },
        new object[] { new UnprocessableContent("cannot") },
        new object[] { new RequestTimeout("slow") },
        new object[] { new RateLimited(TimeSpan.FromSeconds(30), "slow down") },
        new object[] { new InternalServerError("boom") },
        new object[] { new NotImplemented("later") },
        new object[] { new BadGateway("upstream") },
        new object[] { new ServiceUnavailable(TimeSpan.FromMinutes(2), "draining") },
        new object[] { new GatewayTimeout("upstream slow") }
    };

    /// <summary>
    /// The one assertion worth making across all of them: the MessagePack body carries exactly the
    /// members the JSON body does, under exactly the same names.
    /// </summary>
    [Theory]
    [MemberData(nameof(Bodies))]
    public void TheMembersAreTheOnesJsonWrites(object body) {
        var asJson = JsonSerializer.SerializeToElement(body, body.GetType(), Web);

        var asMessagePack = JsonDocument.Parse(
            MessagePackSerializer.ConvertToJson(
                MessagePackSerializer.Serialize(body.GetType(), body, Options, TestContext.Current.CancellationToken),
                Options,
                TestContext.Current.CancellationToken)).RootElement;

        Assert.Equal(
            asJson.EnumerateObject().Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal),
            asMessagePack.EnumerateObject().Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    /// <summary>
    /// And the values agree for the three members every one of them decides for itself, which is
    /// what a caller switches on.
    /// </summary>
    [Theory]
    [MemberData(nameof(Bodies))]
    public void TheProblemIdentityAgreesWithJson(object body) {
        var asJson = JsonSerializer.SerializeToElement(body, body.GetType(), Web);

        var asMessagePack = JsonDocument.Parse(
            MessagePackSerializer.ConvertToJson(
                MessagePackSerializer.Serialize(body.GetType(), body, Options, TestContext.Current.CancellationToken),
                Options,
                TestContext.Current.CancellationToken)).RootElement;

        foreach (var member in new[] { "type", "title", "status", "detail" }) {
            Assert.Equal(
                asJson.GetProperty(member).ToString(),
                asMessagePack.GetProperty(member).ToString());
        }
    }

    private static T RoundTrip<T>(T value) =>
        MessagePackSerializer.Deserialize<T>(
            MessagePackSerializer.Serialize(value, Options, TestContext.Current.CancellationToken),
            Options,
            TestContext.Current.CancellationToken);

    [Fact]
    public void ANotFoundRoundTrips() {
        var read = RoundTrip(new NotFound("todo", "No todo has id 9."));

        Assert.Equal("todo", read.Resource);
        Assert.Equal("No todo has id 9.", read.Detail);
        Assert.Equal(404, read.Status);
    }

    [Fact]
    public void ADetailOnlyBodyRoundTrips() {
        var read = RoundTrip(new Conflict("A todo titled 'x' already exists."));

        Assert.Equal("A todo titled 'x' already exists.", read.Detail);
        Assert.Equal(409, read.Status);
    }

    [Fact]
    public void ANullDetailStaysNull() => Assert.Null(RoundTrip(new Conflict()).Detail);

    /// <summary>
    /// A duration goes out as the string System.Text.Json writes, not as a count of seconds - the
    /// schema says string, and a body carrying a number would not match it.
    /// </summary>
    [Fact]
    public void ADurationIsWrittenTheWayJsonWritesOne() {
        var json = MessagePackSerializer.ConvertToJson(
            MessagePackSerializer.Serialize(
                new RateLimited(TimeSpan.FromSeconds(30)), Options, TestContext.Current.CancellationToken),
            Options,
            TestContext.Current.CancellationToken);

        Assert.Contains("\"00:00:30\"", json);
        Assert.Equal(TimeSpan.FromSeconds(30), RoundTrip(new RateLimited(TimeSpan.FromSeconds(30))).RetryAfter);
    }

    [Fact]
    public void AnAbsentDurationRoundTripsAsAbsent() =>
        Assert.Null(RoundTrip(new ServiceUnavailable(Detail: "draining")).After);

    /// <summary>
    /// The challenge comes back through <c>Parse</c>, which is the only public way into the type -
    /// so the header value is what is trusted on the way in, and the members derived from it are
    /// written for a reader rather than read back.
    /// </summary>
    [Fact]
    public void AChallengeRoundTripsThroughItsHeaderValue() {
        var challenge = AuthorizationChallenge.InvalidToken("realm", "expired");
        var read = RoundTrip(new Unauthorized("who", challenge));

        Assert.NotNull(read.Challenge);
        Assert.Equal(challenge.HeaderValue, read.Challenge!.HeaderValue);
        Assert.Equal(challenge.Scheme, read.Challenge.Scheme);
        Assert.Equal(challenge.Error, read.Challenge.Error);
    }

    [Fact]
    public void AnUnauthorizedWithNoChallengeRoundTrips() =>
        Assert.Null(RoundTrip(new Unauthorized("who")).Challenge);

    /// <summary>
    /// A member a later version adds is skipped rather than refused, so a client one version behind
    /// still reads the body.
    /// </summary>
    [Fact]
    public void AnUnknownMemberIsSkipped() {
        var bytes = MessagePackSerializer.ConvertFromJson(
            "{\"resource\":\"todo\",\"detail\":\"d\",\"type\":\"t\",\"title\":\"T\",\"status\":404,\"instance\":\"/x\"}",
            Options,
            TestContext.Current.CancellationToken);

        var read = MessagePackSerializer.Deserialize<NotFound>(
            bytes, Options, TestContext.Current.CancellationToken);

        Assert.Equal("todo", read.Resource);
        Assert.Equal("d", read.Detail);
    }
}
