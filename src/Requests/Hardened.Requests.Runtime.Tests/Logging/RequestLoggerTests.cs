using Hardened.Requests.Abstract.Timeouts;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.Logging;
using Hardened.Requests.Runtime.Tests.Support;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Logging;

/// <summary>
/// One line per failed request, at the level the answer decides.
/// </summary>
/// <remarks>
/// A malformed body, a non-integer path value, an unsupported coding and a deadline the operation
/// declared were all logged at Error with a stack trace, and the bind failures twice - once as
/// "failed to bind parameters" and again as "request failed", with the same stack. A 400 is the
/// caller's mistake and a declared 504 is the operation's own decision; neither is a fault, and a
/// log that reports them as faults buries the ones that are.
/// </remarks>
public class RequestLoggerTests
{
    private sealed class Line
    {
        public LogLevel Level { get; init; }
        public string Message { get; init; } = "";
        public Exception? Exception { get; init; }
        public IReadOnlyDictionary<string, object?> Fields { get; init; } =
            new Dictionary<string, object?>();
    }

    private sealed class Capturing : ILogger<RequestLogger>
    {
        public List<Line> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) =>
            Lines.Add(
                new Line
                {
                    Level = logLevel,
                    Message = formatter(state, exception),
                    Exception = exception,
                    Fields = state is IEnumerable<KeyValuePair<string, object?>> fields
                        ? fields.ToDictionary(f => f.Key, f => f.Value)
                        : new Dictionary<string, object?>(),
                }
            );
    }

    private static (RequestLogger Logger, Capturing Log) Logger()
    {
        var capturing = new Capturing();

        return (new RequestLogger(capturing), capturing);
    }

    private static void Started(Abstract.Execution.IExecutionContext context) =>
        context.Response.Body.WriteByte((byte)'{');

    [Fact]
    public void ARefusalIsOneWarningLineWithoutAStack()
    {
        var (logger, log) = Logger();
        var context = Pipeline.Context("POST", "/todos");

        context.Response.Status = 400;

        logger.RequestFailed(
            context,
            new FormatException("The input string 'abc' was not in a correct format.")
        );

        var line = Assert.Single(log.Lines);

        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.Null(line.Exception);
        Assert.Contains("POST /todos refused with 400", line.Message);
        Assert.Contains("not in a correct format", line.Message);
    }

    /// <summary>A throws-mode 404 is a refusal too, however it was raised.</summary>
    [Fact]
    public void AThrownNotFoundIsAWarning()
    {
        var (logger, log) = Logger();
        var context = Pipeline.Context(path: "/todos/999");

        context.Response.Status = 404;

        logger.RequestFailed(context, new InvalidOperationException("No todo has id 999."));

        Assert.Equal(LogLevel.Warning, Assert.Single(log.Lines).Level);
    }

    [Fact]
    public void AServerFaultIsAnErrorWithTheException()
    {
        var (logger, log) = Logger();
        var context = Pipeline.Context();
        var fault = new InvalidOperationException("boom");

        context.Response.Status = 500;

        logger.RequestFailed(context, fault);

        var line = Assert.Single(log.Lines);

        Assert.Equal(LogLevel.Error, line.Level);
        Assert.Same(fault, line.Exception);
    }

    /// <summary>The hosts log an escaped exception before any status is assigned; that is a fault.</summary>
    [Fact]
    public void AFailureWithNoStatusIsAnError()
    {
        var (logger, log) = Logger();

        logger.RequestFailed(Pipeline.Context(), new InvalidOperationException("boom"));

        Assert.Equal(LogLevel.Error, Assert.Single(log.Lines).Level);
    }

    /// <summary>
    /// A budget the operation declared, answered with the status it declared, is not a fault and
    /// not a stack ending in Task.Delay: it is one line saying how long the operation was given.
    /// </summary>
    [Theory]
    [InlineData(504)]
    [InlineData(503)]
    public void ADeclaredDeadlineIsOneWarningLineNamingTheBudget(int status)
    {
        var (logger, log) = Logger();
        var context = Pipeline.Context(path: "/rates");

        context.HandlerInfo = new ExecutionRequestHandlerInfo(
            "/rates",
            "GET",
            typeof(object),
            "Read",
            timeout: new TimeoutPolicy(200, status)
        );
        context.Response.Status = status;

        logger.RequestFailed(context, new TaskCanceledException());

        var line = Assert.Single(log.Lines);

        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.Null(line.Exception);
        Assert.Contains("GET /rates did not finish inside its 200 ms budget", line.Message);
        Assert.Contains(status.ToString(), line.Message);
    }

    /// <summary>A cancellation on a handler nothing bounded is whatever the converter made of it.</summary>
    [Fact]
    public void ACancellationWithNoBudgetIsAnError()
    {
        var (logger, log) = Logger();
        var context = Pipeline.Context();

        context.Response.Status = 500;

        logger.RequestFailed(context, new TaskCanceledException());

        Assert.Equal(LogLevel.Error, Assert.Single(log.Lines).Level);
    }

    /// <summary>
    /// A cancellation of the request's own token is the caller leaving or the host ending the
    /// request, not a fault. A client that walked away from a stream used to leave an Error line
    /// with a stack through the heartbeat's delay.
    /// </summary>
    [Fact]
    public void ACancellationOfTheRequestsOwnTokenIsOneInformationLine()
    {
        var (logger, log) = Logger();
        using var cancellation = new CancellationTokenSource();
        var context = Pipeline.Cancellable(cancellation.Token);

        context.Response.Status = 200;
        Started(context);
        cancellation.Cancel();

        logger.RequestFailed(context, new TaskCanceledException());

        var line = Assert.Single(log.Lines);

        Assert.Equal(LogLevel.Information, line.Level);
        Assert.Equal("GET / was cancelled before it finished", line.Message);
        Assert.Null(line.Exception);
    }

    /// <summary>
    /// After the response has started the status on it is the one already sent, whatever it says,
    /// and a failure past that point tore the body: a fault.
    /// </summary>
    [Fact]
    public void AFailureAfterTheResponseStartedIsAnErrorWhateverTheStatus()
    {
        var (logger, log) = Logger();
        var context = Pipeline.Context();

        context.Response.Status = 200;
        Started(context);

        logger.RequestFailed(context, new IOException("torn"));

        Assert.Equal(LogLevel.Error, Assert.Single(log.Lines).Level);
    }

    /// <summary>
    /// The bind line is the detail behind the one RequestFailed writes once the status is known,
    /// not a second Error with the same stack.
    /// </summary>
    [Fact]
    public void ABindFailureIsLoggedAtDebug()
    {
        var (logger, log) = Logger();

        logger.RequestParameterBindFailed(Pipeline.Context(), new FormatException("bad"));

        Assert.Equal(LogLevel.Debug, Assert.Single(log.Lines).Level);
    }

    /// <summary>
    /// Nothing assigns a status on an ordinary success path, and the transport answers 200. The
    /// finished line used to leave <c>{statusCode}</c> unfilled for it, and carried the duration
    /// as a <see cref="TimeSpan"/> under a field named <c>durationMs</c>.
    /// </summary>
    [Fact]
    public void AFinishedRequestWithNoStatusLogs200AndMilliseconds()
    {
        var (logger, log) = Logger();

        logger.RequestEnd(Pipeline.Context(path: "/reports/1"));

        var line = Assert.Single(log.Lines);

        Assert.Equal(LogLevel.Information, line.Level);
        Assert.StartsWith("GET /reports/1 finished status code '200' duration ", line.Message);
        Assert.EndsWith(" ms", line.Message);
        Assert.DoesNotContain("  ", line.Message);
        Assert.Equal(200, line.Fields["statusCode"]);
        Assert.IsType<double>(line.Fields["durationMs"]);
    }

    [Fact]
    public void AFinishedRequestLogsTheStatusItWasGiven()
    {
        var (logger, log) = Logger();
        var context = Pipeline.Context();

        context.Response.Status = 404;

        logger.RequestEnd(context);

        Assert.Equal(404, Assert.Single(log.Lines).Fields["statusCode"]);
    }
}
