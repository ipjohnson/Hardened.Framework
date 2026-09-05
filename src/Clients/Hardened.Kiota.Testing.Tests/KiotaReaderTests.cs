using System.Net;
using System.Text;
using Hardened.Kiota.Testing;
using Hardened.Requests.Abstract.Responses;
using Hardened.Web.Testing;
using Microsoft.Kiota.Abstractions;
using Xunit;

// The route under test, named the way a test project names it, so Returns reads through it.
[assembly: KiotaTesting]

namespace Hardened.Kiota.Testing.Tests;

/// <summary>
/// <c>Returns</c> and <c>ReturnsStatus</c> read through the Kiota route, over the two things a
/// Kiota call produces: a thrown model, or a returned body with the response recorded one hop
/// below it.
/// </summary>
/// <remarks>
/// No generated client and no pipeline here. The tasks are built by hand in the shapes Kiota's
/// generated methods complete with, so each test is about one reading rule; the route and the
/// real client are driven through the Web integration application's suite.
/// </remarks>
public class KiotaReaderTests {

    /// <summary>What Kiota throws for a status the document declares a body for: the model itself.</summary>
    private sealed class Problem : ApiException {
        public string? Detail { get; init; }
    }

    private sealed class Todo {
        public int Id { get; init; }
    }

    #region refusals, read off the thrown model

    [Fact]
    public async Task ARefusalIsTheThrownModelAtTheDeclaredStatus() {
        var thrown = new Problem { ResponseStatusCode = 404, Detail = "No todo has id 9999." };

        var refused = await Task.FromException<Todo>(thrown).Returns<NotFound<Problem>>();

        Assert.Same(thrown, refused.Body);
        Assert.Equal("No todo has id 9999.", refused.Body.Detail);
    }

    [Fact]
    public async Task ARefusalAtAnotherStatusFailsNamingBoth() {
        var thrown = new Problem { ResponseStatusCode = 409 };

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Task.FromException<Todo>(thrown).Returns<NotFound<Problem>>());

        Assert.Contains("Expected 404 (NotFound<Problem>)", failure.Message);
        Assert.Contains("answered 409 carrying a Problem", failure.Message);
    }

    /// <summary>
    /// Kiota throws its base exception for a status the document declares no body for, so there
    /// was nothing to deserialise into - which the failure says, rather than only that the body is
    /// missing.
    /// </summary>
    [Fact]
    public async Task AnUntypedRefusalSaysWhyThereIsNoBody() {
        var thrown = new ApiException("refused") { ResponseStatusCode = 404 };

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Task.FromException<Todo>(thrown).Returns<NotFound<Problem>>());

        Assert.Contains("carried none", failure.Message);
        Assert.Contains("bare ApiException", failure.Message);
    }

    [Fact]
    public async Task AnUntypedRefusalStillAnswersItsStatus() {
        var thrown = new ApiException("refused") { ResponseStatusCode = 404 };

        await Task.FromException<Todo>(thrown).ReturnsStatus<NotFound>();
    }

    [Fact]
    public async Task AStatusMismatchOnReturnsStatusNamesBothAndTheAbsentBody() {
        var thrown = new ApiException("refused") { ResponseStatusCode = 409 };

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Task.FromException<Todo>(thrown).ReturnsStatus<NotFound>());

        Assert.Contains("Expected 404 (NotFound)", failure.Message);
        Assert.Contains("answered 409 with no body", failure.Message);
    }

    /// <summary>
    /// The headers a refusal carries are read off the thrown model, because that is what the
    /// client surfaced - a client that dropped them fails here, whatever the transport saw.
    /// </summary>
    [Fact]
    public async Task ARefusalReadsItsHeadersOffTheThrownModel() {
        var thrown = new Problem { ResponseStatusCode = 429 };
        thrown.ResponseHeaders["Retry-After"] = ["30"];

        var limited = await Task.FromException<Todo>(thrown).Returns<RateLimited<Problem>>();

        Assert.Equal(TimeSpan.FromSeconds(30), limited.RetryAfter);
    }

    [Fact]
    public async Task AMultiValuedHeaderIsReadAsOneLine() {
        var thrown = new Problem { ResponseStatusCode = 405 };
        thrown.ResponseHeaders["Allow"] = ["GET", "HEAD"];

        var refused = await Task.FromException<Todo>(thrown).Returns<MethodNotAllowed<Problem>>();

        Assert.Equal("GET, HEAD", refused.Allow);
    }

    /// <summary>A failure that is not the client refusing is not an answer, and is not caught.</summary>
    [Fact]
    public async Task AnExceptionThatIsNotARefusalPropagates() {
        await Assert.ThrowsAsync<TimeoutException>(
            () => Task.FromException<Todo>(new TimeoutException()).Returns<NotFound<Problem>>());
    }

    #endregion

    #region successes, read off what the client received

    [Fact]
    public async Task ASuccessReadsItsStatusAndHeadersFromWhatTheClientReceived() {
        await Receive(201, ("Location", "/todos/7"));

        var created = await Task.FromResult(new Todo { Id = 7 }).Returns<Created<Todo>>();

        Assert.Equal(7, created.Value.Id);
        Assert.Equal("/todos/7", created.Location);
    }

    /// <summary>A generated delete completes with no value, which is not a body of nothing.</summary>
    [Fact]
    public async Task AMethodReturningNothingIsNotABody() {
        await Receive(204);

        await Deleted().Returns<NoContent>();
    }

    [Fact]
    public async Task ASuccessAtAnotherStatusFailsNamingBoth() {
        await Receive(200);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Task.FromResult(new Todo()).Returns<Created<Todo>>());

        Assert.Contains("Expected 201 (Created<Todo>)", failure.Message);
        Assert.Contains("answered 200 carrying a Todo", failure.Message);
    }

    /// <summary>Content headers are headers too; only the transport draws the line between them.</summary>
    [Fact]
    public async Task ContentHeadersAreReadWithTheRest() {
        await Receive(200, ("ETag", "\"abc\""));

        var answer = await Task.FromResult(new Todo()).Returns<Ok<Todo>>();

        Assert.Equal("\"abc\"", answer.Headers!["ETag"]);
        Assert.StartsWith("application/json", answer.Headers["Content-Type"]);
    }

    /// <summary>
    /// A success with nothing recorded is not this route's to read, and the failure says what a
    /// recorded client is.
    /// </summary>
    [Fact]
    public async Task WithNothingReceivedASuccessCannotBeRead() {
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Task.FromResult(new Todo()).Returns<Ok<Todo>>());

        Assert.Contains("no route that read this call, which returned a Todo", failure.Message);
        Assert.Contains("[assembly: KiotaTesting]", failure.Message);
    }

    [Fact]
    public async Task ReturnsStatusReadsASuccessTheSameWay() {
        await Receive(204);

        await Deleted().ReturnsStatus<NoContent>();
    }

    /// <summary>Within one test the recording is the most recent call, which is the one being asserted.</summary>
    [Fact]
    public async Task TheMostRecentCallIsTheOneRead() {
        await Receive(201, ("Location", "/todos/1"));
        await Receive(204);

        await Deleted().Returns<NoContent>();
    }

    private static async Task Deleted() => await Task.Yield();

    /// <summary>
    /// A call through the handler the route puts in a client's chain, so the response is recorded
    /// for this test the way a generated client's would be.
    /// </summary>
    private static async Task Receive(int status, params (string Name, string Value)[] headers) {
        using var http = new HttpClient(new RecordingHandler { InnerHandler = new Answering(status, headers) }) {
            BaseAddress = new Uri("http://harness/")
        };

        using var response = await http.GetAsync("/anything", TestContext.Current.CancellationToken);
    }

    private sealed class Answering(int status, (string Name, string Value)[] headers) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) {
            var response = new HttpResponseMessage((HttpStatusCode)status) {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };

            foreach (var (name, value) in headers) {
                response.Headers.TryAddWithoutValidation(name, value);
            }

            return Task.FromResult(response);
        }
    }

    #endregion
}
