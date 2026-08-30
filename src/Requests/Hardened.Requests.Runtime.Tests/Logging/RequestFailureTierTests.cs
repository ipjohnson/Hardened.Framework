using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Responses;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Errors;
using Hardened.Requests.Runtime.Logging;
using Hardened.Requests.Runtime.RateLimiting;
using Hardened.Requests.Runtime.Tests.Support;
using Hardened.Requests.Runtime.Validation;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Logging;

/// <summary>
/// What a failed request is written down as, which depends on how much <see cref="RequestLogger"/>
/// knows about the exception.
///
/// <para>
/// Every failure used to be reported the same way - <c>LogError</c> with the exception, so a stack
/// trace - and most failures are not faults. A declared 404, a validation failure, an authorization
/// refusal and a rate-limit refusal all reach <c>RequestFailed</c>, so an application answering
/// exactly as designed filled its log with stacks and its error count with its own correct
/// responses.
/// </para>
/// </summary>
public class RequestFailureTierTests {

    // ------------------------------------------------------ tier one: known

    /// <summary>
    /// The response, not the base class's "The request produced status 404." - which says the one
    /// thing the status code already said and nothing about what was missing.
    /// </summary>
    [Fact]
    public void AThrownResponseIsReportedAsTheResponse() {
        var entry = Report(new NotFound("order 42").AsException());

        Assert.Contains("NotFound", entry.Message);
        Assert.Contains("order 42", entry.Message);
    }

    /// <summary>
    /// Information, not Error. A 404 is the caller asking for something that is not there, and an
    /// application that answers one is working.
    /// </summary>
    [Fact]
    public void AThrownResponseIsNotAnError() {
        Assert.Equal(LogLevel.Information, Report(new NotFound("order 42").AsException()).Level);
    }

    /// <summary>
    /// No exception on the entry, so no stack. The throw site of a deliberate throw is the line
    /// that threw, which nobody needs.
    /// </summary>
    [Fact]
    public void AThrownResponseCarriesNoStack() {
        Assert.Null(Report(new NotFound("order 42").AsException()).Exception);
    }

    /// <summary>
    /// A thrown 503 is still a fault. Severity follows the status the caller is answered with
    /// rather than the tier, so recognising an exception does not quiet a server error.
    /// </summary>
    [Fact]
    public void AThrownServerErrorIsStillAnError() {
        Assert.Equal(
            LogLevel.Error, Report(new ServiceUnavailable(Detail: "maintenance").AsException()).Level);
    }

    /// <summary>
    /// The fields, which is what the caller was answered with. Without them the line says a request
    /// was invalid and sends whoever reads it to the response body to find out how.
    /// </summary>
    [Fact]
    public void AValidationFailureNamesTheFieldsThatFailed() {
        var result = ValidationModules.ValidationResult.FromErrors(new[] {
            new ValidationModules.ValidationError("email", "Required", "email is required"),
            new ValidationModules.ValidationError("age", "Range", "age must be between 0 and 120")
        });

        var entry = Report(new ValidationException(result));

        Assert.Contains("email is required", entry.Message);
        Assert.Contains("age must be between 0 and 120", entry.Message);
        Assert.Equal(LogLevel.Information, entry.Level);
    }

    /// <summary>
    /// Both routes to a validation failure, which are the same two <c>ExceptionToModelConverter</c>
    /// maps: the filter throws Hardened's exception, a handler calling <c>ValidateAndThrow</c>
    /// throws ValidationModules'. The second derives from neither framework base, so without being
    /// named here it would be reported as an unhandled fault while the caller got a 400.
    /// </summary>
    [Fact]
    public void AValidationFailureFromValidationModulesIsRecognisedToo() {
        var result = ValidationModules.ValidationResult.FromErrors(new[] {
            new ValidationModules.ValidationError("email", "Required", "email is required")
        });

        var entry = Report(new ValidationModules.ValidationException(result));

        Assert.Contains("email is required", entry.Message);
        Assert.Equal(LogLevel.Information, entry.Level);
    }

    /// <summary>
    /// The challenge, which names the scheme and the grants that would have worked. The exception's
    /// own message is deliberately unspecific because it is echoed to the caller.
    /// </summary>
    [Fact]
    public void AnAuthorizationRefusalIsReportedAsItsChallenge() {
        var entry = Report(
            new AuthorizationException(AuthorizationChallenge.InsufficientScope(["orders:write"])));

        Assert.Contains("orders:write", entry.Message);
        Assert.Contains("403", entry.Message);
        Assert.Equal(LogLevel.Information, entry.Level);
    }

    /// <summary>
    /// A refusal the framework issues itself, and the case a rate limiter exists to make ordinary.
    /// Reported at Error it would put every throttled caller on the error count.
    /// </summary>
    [Fact]
    public void ARateLimitRefusalIsNotAnError() {
        var entry = Report(
            new RateLimitExceededException(
                RateLimitDecision.Refuse(100, TimeSpan.FromSeconds(30))));

        Assert.Contains("429", entry.Message);
        Assert.Equal(LogLevel.Information, entry.Level);
    }

    /// <summary>
    /// <c>NotAcceptableException</c> writes its own message for a log as well as a client, and says
    /// so in its remarks, so the description is that message.
    /// </summary>
    [Fact]
    public void ANotAcceptableIsReportedAsWhatTheOperationProduces() {
        var entry = Report(new NotAcceptableException(["application/json"]));

        Assert.Contains("application/json", entry.Message);
        Assert.Contains("406", entry.Message);
    }

    /// <summary>
    /// A validation failure carrying no field errors falls back to its own message. An empty list
    /// should not happen, and reporting it as an empty description would be the one line nobody
    /// could act on.
    /// </summary>
    [Fact]
    public void AValidationFailureThatNamesNoFieldsFallsBackToItsMessage() {
        var entry = Report(new ValidationException(ValidationModules.ValidationResult.FromErrors([])));

        Assert.Contains("One or more validation errors occurred.", entry.Message);
    }

    /// <summary>
    /// A Content-Encoding the deserializers do not support, which is the caller's mistake and is
    /// already spelled out by the exception's own message.
    /// </summary>
    [Fact]
    public void ABadContentEncodingIsReportedAsItsMessage() {
        var entry = Report(new BadContentEncodingException("br"));

        Assert.Contains("br", entry.Message);
        Assert.Contains("400", entry.Message);
        Assert.Equal(LogLevel.Information, entry.Level);
    }

    /// <summary>
    /// A recognised failure keeps its inner exception, which is the one stack it has worth having.
    /// <c>ValidationException</c> carries one for exactly this - "abc is not in a correct format" is
    /// the difference between a diagnosable failure and a bare assertion that something was wrong.
    /// </summary>
    [Fact]
    public void AnInnerExceptionSurvivesWhereTheStackIsDropped() {
        var result = ValidationModules.ValidationResult.FromErrors(new[] {
            new ValidationModules.ValidationError("limit", "Format", "limit is not a valid Int32")
        });

        var cause = new FormatException("abc is not in a correct format");

        Assert.Same(cause, Report(new ValidationException(result, cause)).Exception);
    }

    // --------------------------------------------------- tier two: declared

    /// <summary>
    /// Nothing here can describe an application's own exception, but deriving from a framework base
    /// says the throw was deliberate, so it is named rather than dumped.
    /// </summary>
    [Fact]
    public void AnApplicationsOwnStatusCodeExceptionIsReportedByTypeAndMessage() {
        var entry = Report(new OrderAlreadyShipped());

        Assert.Contains("OrderAlreadyShipped", entry.Message);
        Assert.Contains("order 42 has already shipped", entry.Message);
        Assert.Contains("409", entry.Message);
    }

    /// <summary>Declared, so no stack - the same reason a known failure carries none.</summary>
    [Fact]
    public void AnApplicationsOwnStatusCodeExceptionCarriesNoStack() {
        Assert.Null(Report(new OrderAlreadyShipped()).Exception);
    }

    [Fact]
    public void AnApplicationsOwnStatusCodeExceptionIsNotAnError() {
        Assert.Equal(LogLevel.Information, Report(new OrderAlreadyShipped()).Level);
    }

    /// <summary>
    /// The other base an application derives from to state what its exception means.
    /// <c>BadRequestException</c>'s whole contract is "this is a client error", and that is enough
    /// to keep it off the error count.
    /// </summary>
    [Fact]
    public void AnApplicationsOwnBadRequestIsNotAnError() {
        var entry = Report(new MalformedCursor());

        Assert.Contains("MalformedCursor", entry.Message);
        Assert.Contains("400", entry.Message);
        Assert.Equal(LogLevel.Information, entry.Level);
    }

    /// <summary>
    /// A status of 500 or more is a fault whoever declared it, so a deliberate one is still an
    /// Error. This is what keeps the tier a decision about detail rather than about severity.
    /// </summary>
    [Fact]
    public void AnApplicationsOwnServerErrorIsStillAnError() {
        Assert.Equal(LogLevel.Error, Report(new StatusCodeException(503, message: "draining")).Level);
    }

    // ------------------------------------------------- tier three: unhandled

    /// <summary>
    /// The exception itself, with its stack. This is the one tier where the stack is the whole
    /// value of the entry, because nothing else says where the fault was.
    /// </summary>
    [Fact]
    public void AnUnrecognisedExceptionKeepsItsStack() {
        var failure = new InvalidOperationException("handler blew up");

        Assert.Same(failure, Report(failure).Exception);
    }

    [Fact]
    public void AnUnrecognisedExceptionIsAnError() {
        Assert.Equal(LogLevel.Error, Report(new InvalidOperationException("handler blew up")).Level);
    }

    /// <summary>
    /// Unchanged, so anything reading the old line still finds it. Only the two recognised tiers are
    /// new wording.
    /// </summary>
    [Fact]
    public void AnUnrecognisedExceptionKeepsTheMessageItAlwaysHad() {
        Assert.Equal(
            "GET / request failed", Report(new InvalidOperationException("boom")).Message);
    }

    /// <summary>
    /// Whatever the response says. Status decides severity for the two recognised tiers, and this
    /// tier is the one where it does not: nothing named this status, so nothing vouches for it.
    /// </summary>
    [Fact]
    public void AnUnrecognisedExceptionIsAnErrorWhateverStatusTheResponseCarries() {
        Assert.Equal(
            LogLevel.Error, Report(new InvalidOperationException("boom"), status: 400).Level);
    }

    // ------------------------------------------------------------ the status

    /// <summary>
    /// Read from the response for an exception that names none, so the line reports the status the
    /// caller was actually answered with. <c>ExceptionResponseSerializer</c> assigns it before
    /// reporting the failure for this reason.
    /// </summary>
    [Fact]
    public void TheStatusComesFromTheResponseWhenTheExceptionNamesNone() {
        Assert.Contains("422", Report(new MalformedCursor(), status: 422).Message);
    }

    /// <summary>
    /// And from the exception when it names one, which is where the converter takes it from - so
    /// the log line and the response cannot disagree about what was sent.
    /// </summary>
    [Fact]
    public void TheStatusComesFromTheExceptionWhenItNamesOne() {
        Assert.Contains("409", Report(new OrderAlreadyShipped()).Message);
    }

    // ----------------------------------------------------------------- setup

    /// <summary>
    /// Reports one failure and hands back the single entry it wrote.
    /// </summary>
    /// <param name="status">
    /// What the response already carries when the failure is reported. The default is a host's
    /// catch-all, which reports an exception before anything has answered it.
    /// </param>
    private static Entry Report(Exception exception, int? status = null) {
        var capturing = new CapturingLogger();
        var context = Pipeline.Context();

        context.Response.Status = status;

        new RequestLogger(capturing).RequestFailed(context, exception);

        return Assert.Single(capturing.Entries);
    }

    private sealed record Entry(LogLevel Level, EventId EventId, string Message, Exception? Exception);

    private sealed class CapturingLogger : ILogger<RequestLogger> {
        public List<Entry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) {
            Entries.Add(new Entry(logLevel, eventId, formatter(state, exception), exception));
        }
    }

    /// <summary>An application's own conflict, of the kind this framework knows nothing about.</summary>
    private sealed class OrderAlreadyShipped : StatusCodeException {
        public OrderAlreadyShipped() : base(409, message: "order 42 has already shipped") { }
    }

    /// <summary>The other base, whose contract is only "this is the caller's mistake".</summary>
    private sealed class MalformedCursor : BadRequestException {
        public MalformedCursor() : base("the page cursor is not readable") { }
    }
}
