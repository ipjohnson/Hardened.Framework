using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Runtime.Headers;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Hardened.Requests.Testing.Conformance;

/// <summary>
/// The behaviour every <see cref="IExecutionResponse"/> implementation must exhibit,
/// expressed once and executed against every transport.
///
/// <para>
/// The sibling of <see cref="ExecutionRequestConformanceTests"/>, and the half that was missing.
/// Transport agnosticism is a claim about both directions, and every divergence found so far has
/// been on this one: an ASP.NET-hosted <c>DELETE</c> that answered 200 in the harness and 404 in
/// production, and a cookie that reached the client over HTTP and vanished under test. Both were
/// responses whose every property was correct and whose clients got something else, which is why
/// each assertion here goes through <see cref="IExecutionResponseConformanceAdapter.Complete"/>
/// rather than reading the values back.
/// </para>
///
/// <para>
/// To enrol a transport, derive from this class and supply an adapter:
/// </para>
/// <code>
/// public class MyTransportResponseConformanceTests : ExecutionResponseConformanceTests {
///     protected override IExecutionResponseConformanceAdapter Adapter { get; } = new MyAdapter();
/// }
/// </code>
/// </summary>
public abstract class ExecutionResponseConformanceTests {

    protected abstract IExecutionResponseConformanceAdapter Adapter { get; }

    private string Because(string what) => $"[{Adapter.TransportName}] {what}";

    private async Task<ObservedResponse> Write(Action<IExecutionResponse> configure) {
        var response = Adapter.CreateResponse();

        configure(response);

        return await Adapter.Complete(response);
    }

    // ---------------------------------------------------------------- status

    /// <summary>
    /// The assertion the ASP.NET host failed. A status with no body behind it is the entire
    /// response for a 204, a 304, a 405 and a redirect, and it has to survive completion.
    /// </summary>
    [Theory]
    [InlineData(200)]
    [InlineData(204)]
    [InlineData(302)]
    [InlineData(404)]
    [InlineData(405)]
    [InlineData(500)]
    public async Task StatusSurvivesCompletionWithNoBody(int status) {
        var observed = await Write(response => response.Status = status);

        Assert.True(status == observed.StatusCode,
            Because($"expected status {status} to reach the client but got {observed.StatusCode}"));
    }

    [Fact]
    public async Task StatusSurvivesCompletionWithABody() {
        var observed = await Write(response => {
            response.Status = 201;
            response.Body.Write("created"u8);
        });

        Assert.Equal(201, observed.StatusCode);
        Assert.Equal("created", observed.BodyAsText());
    }

    /// <summary>
    /// A response nobody gave a status still has one on the wire — there is no such thing as an
    /// HTTP response without a status line, so every transport supplies the same default rather
    /// than passing the null along.
    /// </summary>
    [Fact]
    public async Task AnUnsetStatusBecomesTwoHundred() {
        var observed = await Write(_ => { });

        Assert.True(observed.StatusCode == 200,
            Because($"an unset status must send 200 but sent {observed.StatusCode}"));
    }

    // ---------------------------------------------------------------- headers and body

    [Fact]
    public async Task HeadersSurviveCompletion() {
        var observed = await Write(response =>
            response.Headers["X-Correlation-Id"] = new StringValues("abc-123"));

        Assert.Equal("abc-123", observed.Header("X-Correlation-Id"));
    }

    [Fact]
    public async Task ContentTypeSurvivesCompletion() {
        var observed = await Write(response => response.ContentType = "application/json");

        Assert.NotNull(observed.Header("Content-Type"));
        Assert.Contains("application/json", observed.Header("Content-Type")!);
    }

    [Fact]
    public async Task BodySurvivesCompletion() {
        var observed = await Write(response => response.Body.Write("conformance-body"u8));

        Assert.Equal("conformance-body", observed.BodyAsText());
    }

    [Fact]
    public async Task AnEmptyBodyIsEmptyRatherThanAbsent() {
        var observed = await Write(response => response.Status = 204);

        Assert.NotNull(observed.Body);
        Assert.Empty(observed.Body);
    }

    // ---------------------------------------------------------------- cookies

    /// <summary>
    /// The assertion the test harness failed. <c>Response.Cookies.Append</c> compiled and ran on
    /// every transport; on two of them it wrote a header and on the others it filled a dictionary
    /// nothing read, so a cookie that worked in production could not be tested at all.
    /// </summary>
    [Fact]
    public async Task AnAppendedCookieReachesTheClient() {
        var observed = await Write(response => response.Cookies.Append("session", "abc123"));

        Assert.True(observed.SetCookies.Count > 0,
            Because("Cookies.Append must produce something the client receives, and produced nothing"));
        Assert.Contains(observed.SetCookies, cookie => cookie.StartsWith("session=abc123"));
    }

    [Fact]
    public async Task EveryAppendedCookieReachesTheClient() {
        var observed = await Write(response => {
            response.Cookies.Append("first", "1");
            response.Cookies.Append("second", "2");
        });

        Assert.Equal(2, observed.SetCookies.Count);
        Assert.Contains(observed.SetCookies, cookie => cookie.StartsWith("first=1"));
        Assert.Contains(observed.SetCookies, cookie => cookie.StartsWith("second=2"));
    }

    /// <summary>
    /// The attributes are the security-relevant half of a cookie. A transport that carries the
    /// name and value and drops <c>HttpOnly</c> has produced a different cookie.
    /// </summary>
    [Fact]
    public async Task CookieAttributesReachTheClient() {
        var observed = await Write(response => response.Cookies.Append(
            "session", "abc123",
            new CookieSetOptions(Path: "/app", HttpOnly: true, Secure: true)));

        var cookie = Assert.Single(observed.SetCookies);

        Assert.Contains("Path=/app", cookie);
        Assert.Contains("HttpOnly", cookie);
        Assert.Contains("Secure", cookie);
    }

    [Fact]
    public async Task NoCookiesMeansNothingIsSent() {
        var observed = await Write(_ => { });

        Assert.Empty(observed.SetCookies);
    }

    // ---------------------------------------------------------------- clone contract

    [Fact]
    public async Task CloneCarriesTheStatus() {
        var response = Adapter.CreateResponse();

        response.Status = 418;

        var observed = await Adapter.Complete(response.Clone());

        Assert.True(observed.StatusCode == 418,
            Because($"Clone must carry the status but the clone sent {observed.StatusCode}"));
    }

    /// <summary>
    /// A fork writes to the same client as the request it forked from, so a cookie appended to a
    /// clone has to arrive. On the HTTP hosts this falls out of the collection being header-backed;
    /// on a transport that records into a dictionary it has to be carried deliberately.
    /// </summary>
    [Fact]
    public async Task ACookieAppendedToACloneReachesTheClient() {
        var response = Adapter.CreateResponse();
        var clone = response.Clone();

        clone.Cookies.Append("forked", "yes");

        var observed = await Adapter.Complete(clone);

        Assert.Contains(observed.SetCookies, cookie => cookie.StartsWith("forked=yes"));
    }

    // ---------------------------------------------------------------- response started

    [Fact]
    public async Task ResponseHasNotStartedBeforeAnythingIsWritten() {
        var response = Adapter.CreateResponse();

        Assert.False(response.ResponseStarted,
            Because("a response nothing has written to must not report itself as started"));

        await Adapter.Complete(response);
    }

    // Deliberately not asserted:
    //
    // - That Clone(IHeaderCollection) replaces the headers. This suite was written asserting it and
    //   the assertion was wrong. The ASP.NET and Kestrel adapters both ignore the argument, and
    //   they are right to: their headers are the HttpResponse's and the response feature's, so a
    //   response handed a different dictionary would write headers that reach nobody - the exact
    //   shape of the cookie defect above. Only the transports that own a plain dictionary can
    //   honour it, which makes it a property of some transports rather than of the contract.
    //
    //   Worth resolving rather than leaving: nothing in either repository passes the argument. It
    //   is a parameter on a shipped interface that two of five implementations act on and no
    //   caller uses, which is the same shape as the status properties on the route attributes.
    //
    // - That ShouldCompress, IsBinary and ShouldSerialize survive Clone. They are flags the
    //   pipeline reads rather than things a client observes, so Complete has nothing to report
    //   them through, and asserting them would mean reading the object back - which is the thing
    //   this suite exists to stop doing.
    //
    // - What a transport does with ExceptionValue. Every host converts it through
    //   IExceptionToModelConverter before writing, so the observable outcome belongs to that
    //   conversion rather than to the response contract.
}
