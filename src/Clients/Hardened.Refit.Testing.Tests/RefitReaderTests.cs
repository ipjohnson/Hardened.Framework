using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using Hardened.Refit.Testing;
using Hardened.Requests.Abstract.Responses;
using Hardened.Web.Testing;
using Refit;
using Xunit;

// The route under test, named the way a test project names it, so Returns reads through it.
[assembly: RefitTesting]

namespace Hardened.Refit.Testing.Tests;

/// <summary>
/// <c>Returns</c> and <c>ReturnsStatus</c> read through the Refit route, over the three things a
/// Refit call produces: an envelope, a thrown <see cref="ApiException"/>, or a body alone.
/// </summary>
/// <remarks>
/// No interface and no pipeline here. The envelopes and exceptions are Refit's own types, built
/// from a response by hand in the shapes its generated implementation completes with, so each
/// test is about one reading rule; the route and a real interface are driven through the Web
/// integration application's suite.
/// </remarks>
public class RefitReaderTests {

    public sealed class Problem {
        [JsonPropertyName("detail")]
        public string? Detail { get; set; }
    }

    public sealed class Todo {
        [JsonPropertyName("id")]
        public int Id { get; set; }
    }

    private static readonly RefitSettings Settings = new();

    #region the envelope, which carries all three

    [Fact]
    public async Task AnEnvelopeSuccessCarriesItsBodyAndItsHeaders() {
        var call = Task.FromResult<IApiResponse<Todo>>(
            await Envelope(201, new Todo { Id = 7 }, ("Location", "/todos/7")));

        var created = await call.Returns<Created<Todo>>();

        Assert.Equal(7, created.Value.Id);
        Assert.Equal("/todos/7", created.Location);
    }

    /// <summary>
    /// Refit has no error mapping: the error body is text on the envelope's exception, read here
    /// as the expectation's type argument through the client's own serializer.
    /// </summary>
    [Fact]
    public async Task AnEnvelopeRefusalIsReadAsTheExpectationsBodyType() {
        var call = Task.FromResult<IApiResponse<Todo>>(
            await Envelope<Todo>(404, "{\"detail\":\"No todo has id 9999.\"}"));

        var refused = await call.Returns<NotFound<Problem>>();

        Assert.Equal("No todo has id 9999.", refused.Body.Detail);
    }

    /// <summary>The non-generic envelope, which a method declared Task&lt;IApiResponse&gt; returns.</summary>
    [Fact]
    public async Task ABareEnvelopeCarriesNoBody() {
        var call = Task.FromResult<IApiResponse>(await Envelope<object>(204, content: null));

        await call.Returns<NoContent>();
    }

    [Fact]
    public async Task AnEnvelopeAtAnotherStatusFailsNamingBoth() {
        var call = Task.FromResult<IApiResponse<Todo>>(
            await Envelope<Todo>(409, "{\"detail\":\"taken\"}"));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => call.Returns<NotFound<Problem>>());

        Assert.Contains("Expected 404 (NotFound<Problem>)", failure.Message);
        Assert.Contains("answered 409 carrying a Problem", failure.Message);
    }

    [Fact]
    public async Task ReturnsStatusReadsAnEnvelopeAndNamesTheTextItCarried() {
        var call = Task.FromResult<IApiResponse<Todo>>(await Envelope<Todo>(409, "{\"detail\":\"taken\"}"));

        await Task.FromResult<IApiResponse<Todo>>(await Envelope<Todo>(404, "{}")).ReturnsStatus<NotFound>();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => call.ReturnsStatus<NotFound>());

        Assert.Contains("answered 409 carrying a String", failure.Message);
    }

    [Fact]
    public async Task ContentHeadersAreReadWithTheRest() {
        var call = Task.FromResult<IApiResponse<Todo>>(await Envelope(200, new Todo(), ("ETag", "\"abc\"")));

        var answer = await call.Returns<Ok<Todo>>();

        Assert.Equal("\"abc\"", answer.Headers!["ETag"]);
        Assert.StartsWith("application/json", answer.Headers["Content-Type"]);
    }

    [Fact]
    public async Task ARetryAfterIsReadFromTheEnvelopesHeaders() {
        var call = Task.FromResult<IApiResponse<Todo>>(
            await Envelope<Todo>(429, "{\"detail\":\"slow down\"}", ("Retry-After", "30")));

        var limited = await call.Returns<RateLimited<Problem>>();

        Assert.Equal(TimeSpan.FromSeconds(30), limited.RetryAfter);
        Assert.Equal("slow down", limited.Body.Detail);
    }

    /// <summary>
    /// A status with no record of its own: the marker is a type argument too, and is not the body.
    /// </summary>
    [Fact]
    public async Task AStatusMarkerIsNotMistakenForTheBodyType() {
        var call = Task.FromResult<IApiResponse<Todo>>(
            await Envelope<Todo>(418, "{\"detail\":\"short and stout\"}"));

        var refused = await call.Returns<Status<Http.ImATeapot, Problem>>();

        Assert.Equal("short and stout", refused.Body.Detail);
    }

    [Fact]
    public async Task ABodyTheSerializerCannotReadFailsNamingTheType() {
        var call = Task.FromResult<IApiResponse<Todo>>(await Envelope<Todo>(404, "not json"));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => call.Returns<NotFound<Problem>>());

        Assert.Contains("404 body could not be read as Problem", failure.Message);
        Assert.NotNull(failure.InnerException);
    }

    #endregion

    #region the other shape: a method returning the body alone

    /// <summary>A refusal throws, and the exception carries what the envelope would have.</summary>
    [Fact]
    public async Task AThrownRefusalIsReadLikeAnEnvelope() {
        var thrown = await Refusal(404, "{\"detail\":\"No todo has id 9999.\"}");

        var refused = await Task.FromException<Todo>(thrown).Returns<NotFound<Problem>>();

        Assert.Equal("No todo has id 9999.", refused.Body.Detail);
    }

    [Fact]
    public async Task AThrownRefusalAnswersItsStatus() {
        await Task.FromException<Todo>(await Refusal(404, "")).ReturnsStatus<NotFound>();
    }

    /// <summary>A success returns the body and nothing else, which cannot be an expectation.</summary>
    [Fact]
    public async Task ASuccessReturnedAloneIsRefusedByName() {
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Task.FromResult(new Todo()).Returns<Ok<Todo>>());

        Assert.Contains("no route that read this call, which returned a Todo", failure.Message);
        Assert.Contains("--use-api-response", failure.Message);
    }

    [Fact]
    public async Task ASuccessReturningNothingIsRefusedTheSameWay() {
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Deleted().Returns<NoContent>());

        Assert.Contains("returned no value", failure.Message);
        Assert.Contains("--use-api-response", failure.Message);
    }

    /// <summary>A failure that is not the client refusing is not an answer, and is not caught.</summary>
    [Fact]
    public async Task AnExceptionThatIsNotARefusalPropagates() {
        await Assert.ThrowsAsync<TimeoutException>(
            () => Task.FromException<Todo>(new TimeoutException()).Returns<NotFound<Problem>>());
    }

    private static async Task Deleted() => await Task.Yield();

    #endregion

    private static Task<ApiResponse<T>> Envelope<T>(
        int status, T? content, params (string Name, string Value)[] headers) where T : class =>
        Task.FromResult(new ApiResponse<T>(Response(status, content == null ? "" : "{}", headers), content, Settings));

    private static async Task<ApiResponse<T>> Envelope<T>(
        int status, string errorContent, params (string Name, string Value)[] headers) where T : class {
        var response = Response(status, errorContent, headers);

        return new ApiResponse<T>(response, null, Settings, await Exception(response));
    }

    private static async Task<ApiException> Refusal(int status, string content) =>
        await Exception(Response(status, content, []));

    private static Task<ApiException> Exception(HttpResponseMessage response) =>
        ApiException.Create(response.RequestMessage!, HttpMethod.Get, response, Settings);

    private static HttpResponseMessage Response(int status, string content, (string Name, string Value)[] headers) {
        var response = new HttpResponseMessage((HttpStatusCode)status) {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://harness/todos"),
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };

        foreach (var (name, value) in headers) {
            response.Headers.TryAddWithoutValidation(name, value);
        }

        return response;
    }
}
