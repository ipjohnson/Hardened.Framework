using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Serializer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Hardened.Web.Runtime.Tests.Serializer;

/// <summary>
/// What a handler returning null answers with.
///
/// <para>
/// This is the framework's most visible per-verb behaviour and, until 2026-08-11, nothing asserted
/// it. A GET or a PUT that finds nothing is a 404; a POST or a DELETE that returns nothing
/// succeeded and is a 200. Any verb the switch does not name falls to 200 as well. Silently
/// flipping one of those turns "not found" into "no content, success" for every route in an
/// application, and no test would have noticed.
/// </para>
///
/// <para>
/// The handler lives in <c>Hardened.Requests.Runtime</c>, but the behaviour it encodes is the web
/// verb matrix, which is why it is asserted from here — see TESTING-PLAN.md §6, <c>web-routing</c>.
/// </para>
/// </summary>
public class NullValueResponseStatusTests {

    /// <summary>
    /// The handler under test, with a serializer locator that is never reached.
    /// </summary>
    /// <remarks>
    /// It is only consulted when the handler info carries a <c>NullResponseBody</c>, which none of
    /// these contexts does - they are about the status and the log line. <c>NullResponseBodyTests</c>
    /// covers the case where a body is written.
    /// </remarks>
    private static NullValueResponseHandler Handler(ILogger<NullValueResponseHandler> logger) =>
        new(logger, Substitute.For<ISerializationLocatorService>());

    /// <summary>
    /// The verb matrix. GET and PUT are the two verbs whose null result means the resource is not
    /// there; everything else means the request did its job and had nothing to say.
    /// </summary>
    [Theory]
    [InlineData("GET", 404)]
    [InlineData("PUT", 404)]
    [InlineData("POST", 200)]
    [InlineData("DELETE", 200)]
    public void TheStatusForANullResultDependsOnTheVerb(string method, int expectedStatus) {
        var context = Context(method);

        Handler().Handle(context);

        Assert.Equal(expectedStatus, context.Response.Status);
    }

    /// <summary>
    /// A verb the switch does not name falls through to 200. PATCH is the one that matters — it is
    /// a routable verb with no case of its own, so a PATCH returning null succeeds rather than
    /// 404ing. Anything a transport can deliver lands here too.
    /// </summary>
    [Theory]
    [InlineData("PATCH")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    [InlineData("get")]
    [InlineData("")]
    public void AnUnnamedVerbFallsThroughTo200(string method) {
        var context = Context(method);

        Handler().Handle(context);

        Assert.Equal(200, context.Response.Status);
    }

    /// <summary>
    /// A handler declaring its own null status wins over the verb matrix, for every verb. This is
    /// the only one of the three status overrides on <c>IExecutionRequestHandlerInfo</c> that
    /// anything reads.
    /// </summary>
    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    public void AHandlerDeclaredNullStatusWinsOverTheVerbMatrix(string method) {
        var context = Context(method, nullResponseStatus: 204);

        Handler().Handle(context);

        Assert.Equal(204, context.Response.Status);
    }

    /// <summary>
    /// A handler present but declaring no null status leaves the verb matrix in charge. The
    /// distinction is <c>HasValue</c>, not the handler being there.
    /// </summary>
    [Fact]
    public void AHandlerWithNoDeclaredNullStatusLeavesTheVerbMatrixInCharge() {
        var context = Context("GET", nullResponseStatus: null, withHandlerInfo: true);

        Handler().Handle(context);

        Assert.Equal(404, context.Response.Status);
    }

    /// <summary>
    /// A request that never reached a handler still gets a status. Middleware runs before routing,
    /// so a null response can be serialized with <c>HandlerInfo</c> still unset.
    /// </summary>
    [Fact]
    public void ARequestWithNoHandlerInfoStillGetsTheVerbStatus() {
        var context = Context("GET", withHandlerInfo: false);

        Handler().Handle(context);

        Assert.Equal(404, context.Response.Status);
    }

    /// <summary>
    /// A 404 is logged, and only a 404. The log line is what turns "the client says it 404s" into a
    /// route that can be found, so a 200 that logs it is noise and a 404 that does not is a support
    /// ticket.
    /// </summary>
    [Fact]
    public void OnlyA404IsLogged() {
        var notFound = new RecordingLogger();
        var found = new RecordingLogger();

        Handler(notFound).Handle(Context("GET"));
        Handler(found).Handle(Context("POST"));

        Assert.Single(notFound.Entries);
        Assert.Empty(found.Entries);
    }

    /// <summary>
    /// The log line names the verb and path, which is the whole reason it is there — a bare
    /// "resource not found" tells nobody which route to go and look at.
    /// </summary>
    [Fact]
    public void TheNotFoundLogNamesTheVerbAndPath() {
        var logger = new RecordingLogger();

        Handler(logger).Handle(Context("GET"));

        var (level, message) = Assert.Single(logger.Entries);

        Assert.Equal(LogLevel.Information, level);
        Assert.Contains("GET", message);
        Assert.Contains("/some/path", message);
    }

    /// <summary>
    /// A handler override of 404 is logged too — the log follows the status that was set, not the
    /// verb that would otherwise have produced it.
    /// </summary>
    [Fact]
    public void AHandlerOverrideOf404IsLoggedAsWell() {
        var logger = new RecordingLogger();

        Handler(logger).Handle(Context("POST", nullResponseStatus: 404));

        Assert.Single(logger.Entries);
    }

    /// <summary>
    /// The mirror of the above: a handler that overrides a GET's 404 down to a success status stops
    /// the request being logged as not found.
    /// </summary>
    [Fact]
    public void AHandlerOverrideAwayFrom404IsNotLogged() {
        var logger = new RecordingLogger();

        Handler(logger).Handle(Context("GET", nullResponseStatus: 204));

        Assert.Empty(logger.Entries);
    }

    /// <summary>
    /// Records what was logged, rather than asserting against a mocked <c>ILogger.Log</c> — the
    /// generic state argument of the logging extension methods is an internal type, so a mock
    /// assertion on it pins the test to a version of the logging package.
    /// </summary>
    private sealed class RecordingLogger : ILogger<NullValueResponseHandler> {
        private readonly List<(LogLevel Level, string Message)> _entries = [];

        public IReadOnlyList<(LogLevel Level, string Message)> Entries => _entries;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            _entries.Add((logLevel, formatter(state, exception)));
    }

    private static NullValueResponseHandler Handler() =>
        Handler(NullLogger<NullValueResponseHandler>.Instance);

    private static IExecutionContext Context(
        string method,
        int? nullResponseStatus = null,
        bool withHandlerInfo = true) {
        var context = Substitute.For<IExecutionContext>();
        var request = Substitute.For<IExecutionRequest>();

        request.Method.Returns(method);
        request.Path.Returns("/some/path");

        context.Request.Returns(request);
        context.Response.Returns(Substitute.For<IExecutionResponse>());

        if (withHandlerInfo) {
            var handlerInfo = Substitute.For<IExecutionRequestHandlerInfo>();

            handlerInfo.NullResponseStatus.Returns(nullResponseStatus);

            context.HandlerInfo.Returns(handlerInfo);
        }
        else {
            context.HandlerInfo.Returns((IExecutionRequestHandlerInfo?)null);
        }

        return context;
    }
}
