using System.Reflection;
using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Responses;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Tests.Responses;

/// <summary>
/// Reading a response type back from what it wrote.
/// </summary>
/// <remarks>
/// <para>
/// The whole of <c>FromResponse</c> is header names and a body cast, and both are the kind of thing
/// that is wrong silently. A record that writes <c>Location</c> and reads <c>location</c> back
/// compiles, and fails only in whichever test first asserted on a 201 - as a missing header on a
/// response that carried one.
/// </para>
/// <para>
/// So the sweep below writes each response through its own <c>ApplyHeaders</c> and reads it back
/// through its own <c>FromResponse</c>. Nothing in it names a header, which is what makes it hold
/// for a response type added later.
/// </para>
/// </remarks>
public class ResponseExpectationTests {

    private sealed record Problem(string Detail);

    #region every expectation, round-tripped through its own headers

    /// <summary>
    /// Every response type that can be read back, closed over a body type where it takes one.
    /// </summary>
    public static TheoryData<Type> Expectations {
        get {
            var data = new TheoryData<Type>();

            foreach (var type in typeof(NotFound).Assembly.GetExportedTypes()) {
                if (type.GetCustomAttribute<HttpStatusAttribute>() == null) {
                    continue;
                }

                var closed = type.IsGenericTypeDefinition
                    ? type.MakeGenericType(typeof(string))
                    : type;

                if (IsExpectation(closed)) {
                    data.Add(closed);
                }
            }

            return data;
        }
    }

    /// <remarks>
    /// Asked of the closed type's interfaces rather than by constructing
    /// <c>IResponseExpectation&lt;closed&gt;</c>, which throws rather than answers no when the
    /// constraint does not hold - and the types this has to answer no for are exactly those.
    /// </remarks>
    private static bool IsExpectation(Type type) =>
        type.GetInterfaces().Any(contract =>
            contract.IsGenericType &&
            contract.GetGenericTypeDefinition() == typeof(IResponseExpectation<>) &&
            contract.GenericTypeArguments[0] == type);

    /// <summary>
    /// What a response writes, it can read back.
    /// </summary>
    /// <remarks>
    /// The rebuilt value is not compared to the original, because two of these cannot be: a
    /// dictionary and an <c>AuthorizationChallenge</c> are both reference-compared by the record
    /// equality that would do it. The members that matter are asserted one type at a time below.
    /// What this covers is the part no per-type test would notice going wrong on a type added
    /// later - that the two halves agree on the header names at all.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Expectations))]
    public void AnExpectation_ReadsBackWhatItWrote(Type type) {
        var response = (IHttpStatusResponse)Instantiate(type);

        var written = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        if (response is IProvidesResponseHeaders provider) {
            provider.ApplyHeaders(written);
        }

        var headers = written.ToDictionary(
            header => header.Key, header => header.Value.ToString(), StringComparer.Ordinal);

        var body = response is ICarriesResponseBody carrier ? carrier.Body : null;

        var rebuilt = type
            .GetMethod(nameof(IResponseExpectation<NoContent>.FromResponse),
                BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [body, headers]);

        Assert.Equal(response.Status, ((IHttpStatusResponse)rebuilt!).Status);
    }

    /// <summary>
    /// The lookup is case-insensitive whatever the caller's dictionary is, because header names on
    /// the wire are and a client reporting <c>location</c> is not reporting a different header.
    /// </summary>
    [Fact]
    public void AHeaderIsFoundWhateverCaseTheClientReportedIt() {
        var created = Created<string>.FromResponse(
            "body", new Dictionary<string, string>(StringComparer.Ordinal) { ["location"] = "/todos/1" });

        Assert.Equal("/todos/1", created.Location);
    }

    #endregion

    #region the members each type reads

    [Fact]
    public void Created_ReadsItsLocation() {
        Assert.Equal(
            new Created<string>("body", "/todos/1"), RoundTrip(new Created<string>("body", "/todos/1")));
    }

    [Fact]
    public void RateLimited_ReadsItsDelay() {
        Assert.Equal(
            new RateLimited<string>(TimeSpan.FromSeconds(30), "body"),
            RoundTrip(new RateLimited<string>(TimeSpan.FromSeconds(30), "body")));
    }

    /// <summary>
    /// A fractional delay is written as whole seconds, rounded up, so it does not survive the
    /// round trip as it was given - and a caller reading it back gets what the client was told to
    /// wait, which is the number that matters.
    /// </summary>
    [Fact]
    public void RateLimited_ReadsTheDelayThatWasSentRatherThanTheOneGiven() {
        Assert.Equal(
            TimeSpan.FromSeconds(2), RoundTrip(new RateLimited<string>(TimeSpan.FromMilliseconds(1500), "b")).RetryAfter);
    }

    [Fact]
    public void ServiceUnavailable_ReadsItsDelayAndItsAbsence() {
        Assert.Equal(
            TimeSpan.FromSeconds(5),
            RoundTrip(new ServiceUnavailable<string>("body", TimeSpan.FromSeconds(5))).After);

        Assert.Null(RoundTrip(new ServiceUnavailable<string>("body")).After);
    }

    [Fact]
    public void MethodNotAllowed_ReadsItsAllowHeader() {
        Assert.Equal("GET, HEAD", RoundTrip(new MethodNotAllowed<string>("body", "GET, HEAD")).Allow);
        Assert.Equal("GET, HEAD", RoundTrip(new MethodNotAllowed("GET, HEAD")).Allow);
    }

    [Fact]
    public void Accepted_ReadsItsLocationAndItsAbsence() {
        Assert.Equal("/jobs/1", RoundTrip(new Accepted("/jobs/1")).Location);
        Assert.Null(RoundTrip(new Accepted()).Location);
    }

    [Fact]
    public void NotModified_ReadsItsETagAndItsAbsence() {
        Assert.Equal("\"abc\"", RoundTrip(new NotModified("\"abc\"")).ETag);
        Assert.Null(RoundTrip(new NotModified()).ETag);
    }

    /// <summary>
    /// The challenge is written as a header and read back as a challenge, so a test asserts on the
    /// scope that was required rather than on the string it was formatted into.
    /// </summary>
    [Fact]
    public void Unauthorized_ReadsItsChallenge() {
        var rebuilt = RoundTrip(
            new Unauthorized<Problem>(
                new Problem("no token"), AuthorizationChallenge.InvalidToken("api", "it expired")));

        Assert.Equal("no token", rebuilt.Body.Detail);
        Assert.Equal("invalid_token", rebuilt.Challenge!.Error);
        Assert.Equal("api", rebuilt.Challenge.Realm);
        Assert.Equal("it expired", rebuilt.Challenge.Description);
    }

    /// <summary>
    /// A 401 constructed with no challenge still sends one, so it reads back with the default
    /// rather than with the null it was built from.
    /// </summary>
    [Fact]
    public void Unauthorized_ReadsBackTheChallengeItSentRatherThanNone() {
        var rebuilt = RoundTrip(new Unauthorized<Problem>(new Problem("no token")));

        Assert.Equal(AuthorizationChallenge.BearerScheme, rebuilt.Challenge!.Scheme);
        Assert.Null(rebuilt.Challenge.Error);
    }

    /// <summary>
    /// Every response header, not only the ones the handler passed. The read side cannot tell them
    /// apart, and a test asking for one wants it either way.
    /// </summary>
    [Fact]
    public void Ok_ReadsEveryHeaderTheResponseCarried() {
        var rebuilt = Ok<string>.FromResponse(
            "body",
            new Dictionary<string, string>(StringComparer.Ordinal) {
                ["ETag"] = "\"abc\"", ["X-Total-Count"] = "2",
            });

        Assert.Equal("body", rebuilt.Value);
        Assert.Equal("\"abc\"", rebuilt.Headers!["ETag"]);
        Assert.Equal("2", rebuilt.Headers["X-Total-Count"]);
    }

    #endregion

    #region what it says when the response is not what was expected

    [Fact]
    public void ABodyThatDidNotArrive_SaysWhatWasDeclared() {
        var thrown = Assert.Throws<InvalidOperationException>(
            () => ResponseExpectation.Body<Problem>(null));

        Assert.Contains("Problem", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("carried none", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The case a test author cannot debug from the client's own error: the status matched, the
    /// body arrived, and it deserialised into a different model than the expectation names.
    /// </summary>
    [Fact]
    public void ABodyOfAnotherType_NamesBothTypes() {
        var thrown = Assert.Throws<InvalidOperationException>(
            () => ResponseExpectation.Body<Problem>("a string"));

        Assert.Contains("Problem", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("String", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingRequiredHeader_ListsTheOnesThatWereThere() {
        var thrown = Assert.Throws<InvalidOperationException>(
            () => Created<string>.FromResponse(
                "body", new Dictionary<string, string>(StringComparer.Ordinal) { ["ETag"] = "\"a\"" }));

        Assert.Contains(KnownHeaders.Location, thrown.Message, StringComparison.Ordinal);
        Assert.Contains("ETag", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingRequiredHeaderOnAResponseWithNone_SaysSo() {
        var thrown = Assert.Throws<InvalidOperationException>(
            () => Created<string>.FromResponse("body", new Dictionary<string, string>()));

        Assert.Contains("nothing", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Retry-After's other legal form. It is a date, turning it into a TimeSpan needs a clock, and
    /// two runs of the same assertion would disagree - so it is refused by name rather than read
    /// as zero.
    /// </summary>
    [Fact]
    public void ARetryAfterThatIsADate_IsRefusedRatherThanRead() {
        var thrown = Assert.Throws<InvalidOperationException>(
            () => RateLimited<string>.FromResponse(
                "body",
                new Dictionary<string, string>(StringComparer.Ordinal) {
                    [KnownHeaders.RetryAfter] = "Wed, 21 Oct 2026 07:28:00 GMT",
                }));

        Assert.Contains("delta-seconds", thrown.Message, StringComparison.Ordinal);
    }

    #endregion

    /// <summary>
    /// Writes a response's headers the way the pipeline does, then reads it back the way a client
    /// testing library does.
    /// </summary>
    private static T RoundTrip<T>(T response)
        where T : IHttpStatusResponse, IResponseExpectation<T> {

        var written = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        if (response is IProvidesResponseHeaders provider) {
            provider.ApplyHeaders(written);
        }

        return T.FromResponse(
            response is ICarriesResponseBody carrier ? carrier.Body : null,
            written.ToDictionary(
                header => header.Key, header => header.Value.ToString(), StringComparer.Ordinal));
    }

    /// <summary>
    /// One of each with whatever its constructor asks for, the same way BuiltInResponseTypeTests
    /// does it. The values are placeholders; only the round trip is asserted on.
    /// </summary>
    private static object Instantiate(Type type) {
        var constructor = type.GetConstructors()
            .OrderBy(c => c.GetParameters().Length)
            .First();

        var arguments = constructor.GetParameters()
            .Select(p => p.ParameterType == typeof(string)
                ? "placeholder"
                : p.ParameterType == typeof(TimeSpan)
                    ? TimeSpan.FromSeconds(1)
                    : p.HasDefaultValue
                        ? p.DefaultValue
                        : Activator.CreateInstance(p.ParameterType))
            .ToArray();

        return constructor.Invoke(arguments);
    }
}
