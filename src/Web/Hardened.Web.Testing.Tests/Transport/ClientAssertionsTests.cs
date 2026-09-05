using System.Runtime.CompilerServices;
using Hardened.Requests.Abstract.Responses;
using Hardened.Web.Testing;
using Hardened.Web.Testing.Tests.Transport;
using Xunit;
using Xunit.Sdk;

[assembly: TestClientRoute(typeof(ReadingRoute))]

namespace Hardened.Web.Testing.Tests.Transport;

/// <summary>What a fake client's method throws for a refusal.</summary>
public sealed class Refusal(int status, object? body, string? caveat = null) : Exception("refused") {
    public int Status { get; } = status;

    public object? Body { get; } = body;

    public string? Caveat { get; } = caveat;
}

/// <summary>What a fake client's method returns for a success.</summary>
public sealed record Reply(int Status, object? Body, IReadOnlyDictionary<string, string> Headers);

/// <summary>An expectation with two type arguments and no way to tell which is the body.</summary>
public sealed record Pair<TA, TB>(TA A, TB B) : IResponseExpectation<Pair<TA, TB>> {
    public static int StatusCode => 200;

    public static Pair<TA, TB> FromResponse(object? body, IReadOnlyDictionary<string, string> headers) =>
        throw new NotSupportedException();
}

/// <summary>
/// A route that reads answers, in the shape a generator's package would: it recognises its own
/// two shapes and nothing else, and remembers the body type it was handed for the test to read.
/// </summary>
public sealed class ReadingRoute : ITestClientRoute, ITestClientReader {

    private static readonly ConditionalWeakTable<ITest, StrongBox<Type?>> BodyTypes = new();

    public static Type? BodyTypeHanded =>
        BodyTypes.TryGetValue(TestContext.Current.Test!, out var box) ? box.Value : null;

    public bool CanBuild(Type clientType) => false;

    public object Build(TestClientContext context, Type clientType) => throw new NotSupportedException();

    public Task<ClientAnswer?> Read(object? result, Exception? thrown, Type? bodyType) {
        BodyTypes.AddOrUpdate(TestContext.Current.Test!, new StrongBox<Type?>(bodyType));

        return Task.FromResult<ClientAnswer?>(thrown switch {
            Refusal refusal => new ClientAnswer(refusal.Status, refusal.Body, Headers(), refusal.Caveat),
            null when result is Reply reply => new ClientAnswer(reply.Status, reply.Body, reply.Headers),
            _ => null,
        });
    }

    public string Unreadable => "The reading route reads a Reply or a Refusal.";

    private static Dictionary<string, string> Headers() => new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// <c>Returns</c> and <c>ReturnsStatus</c> as the harness runs them: the call awaited, the answer
/// read by the first route that recognises it, the expectation built from the answer.
/// </summary>
/// <remarks>
/// The generator packages' own suites cover their readers; this covers the part between the test
/// and the reader, through a route that is nobody's generator.
/// </remarks>
public class ClientAssertionsTests {

    [Fact]
    public async Task AReturnedAnswerIsReadAsTheExpectation() {
        var reply = new Reply(201, "body", new Dictionary<string, string> { ["Location"] = "/todos/1" });

        var created = await Task.FromResult(reply).Returns<Created<string>>();

        Assert.Equal("body", created.Value);
        Assert.Equal("/todos/1", created.Location);
    }

    [Fact]
    public async Task AThrownAnswerIsReadAsTheExpectation() {
        var missing = await Task.FromException<string>(new Refusal(404, "nope")).Returns<NotFound<string>>();

        Assert.Equal("nope", missing.Body);
    }

    [Fact]
    public async Task ReturnsStatusReadsTheSameAnswers() {
        await Task.FromException<string>(new Refusal(404, null)).ReturnsStatus<NotFound>();
        await Task.FromResult(new Reply(204, null, Headers())).ReturnsStatus<NoContent>();
    }

    [Fact]
    public async Task AnotherStatusFailsNamingBoth() {
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Task.FromException<string>(new Refusal(409, "taken")).Returns<NotFound<string>>());

        Assert.Contains("Expected 404 (NotFound<String>)", failure.Message);
        Assert.Contains("answered 409 carrying a String", failure.Message);
    }

    /// <summary>The route's caveat is added to a failure about its answer, and only to a failure.</summary>
    [Fact]
    public async Task TheCaveatIsAddedToAFailure() {
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Task.FromException<string>(new Refusal(404, null, "Nothing was deserialised."))
                .Returns<NotFound<string>>());

        Assert.EndsWith("carried none. Nothing was deserialised.", failure.Message);
        Assert.NotNull(failure.InnerException);

        await Task.FromException<string>(new Refusal(404, null, "Nothing was deserialised.")).ReturnsStatus<NotFound>();
    }

    /// <summary>A failure no route recognises is not a refusal, and reaches the test as it was.</summary>
    [Fact]
    public async Task AnExceptionNoRouteRecognisesIsRethrown() {
        await Assert.ThrowsAsync<TimeoutException>(
            () => Task.FromException<string>(new TimeoutException()).Returns<NotFound<string>>());
    }

    [Fact]
    public async Task AResultNoRouteRecognisesFailsNamingWhatEachRouteReads() {
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Task.FromResult("plain").Returns<Ok<string>>());

        Assert.Contains("Returns<Ok<String>>() has no route that read this call, which returned a String.", failure.Message);
        Assert.Contains("The reading route reads a Reply or a Refusal.", failure.Message);
    }

    [Fact]
    public async Task ACallReturningNothingSaysSo() {
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Nothing().ReturnsStatus<NoContent>());

        Assert.Contains("which returned no value", failure.Message);
    }

    /// <summary>
    /// The body type handed to a reader is the expectation's type argument that is not a status
    /// marker, and nothing for an expectation that declares none or for a status alone.
    /// </summary>
    [Fact]
    public async Task TheBodyTypeIsTheExpectationsTypeArgument() {
        await Task.FromResult(new Reply(200, 5, Headers())).Returns<Ok<int>>();
        Assert.Equal(typeof(int), ReadingRoute.BodyTypeHanded);

        await Task.FromResult(new Reply(418, "tea", Headers())).Returns<Status<Http.ImATeapot, string>>();
        Assert.Equal(typeof(string), ReadingRoute.BodyTypeHanded);

        await Task.FromResult(new Reply(418, null, Headers())).Returns<Status<Http.ImATeapot>>();
        Assert.Null(ReadingRoute.BodyTypeHanded);

        await Task.FromResult(new Reply(204, null, Headers())).Returns<NoContent>();
        Assert.Null(ReadingRoute.BodyTypeHanded);

        await Task.FromResult(new Reply(404, "gone", Headers())).ReturnsStatus<NotFound>();
        Assert.Null(ReadingRoute.BodyTypeHanded);
    }

    /// <summary>An expectation with two candidates for the body is refused before the call is read.</summary>
    [Fact]
    public async Task AnExpectationWithTwoBodyCandidatesIsRefused() {
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Task.FromResult(new Reply(200, "a", Headers())).Returns<Pair<string, int>>());

        Assert.Contains("Pair<String, Int32> has 2 type arguments", failure.Message);
    }

    /// <summary>A task that is not a Task&lt;T&gt; at all - Task.Delay's - carries no value either.</summary>
    [Fact]
    public async Task ATaskWithNoResultTypeReturnsNoValue() {
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Task.Delay(1, Xunit.TestContext.Current.CancellationToken).ReturnsStatus<NoContent>());

        Assert.Contains("which returned no value", failure.Message);
    }

    /// <summary>
    /// The readers come from the running test's assembly, so outside a test there is nothing to
    /// read through, and the failure says so rather than reading through nothing.
    /// </summary>
    [Fact]
    public async Task OutsideARunningTestItSaysSo() {
        Task<Exception?> probe;

        // A thread with no execution context flowed into it has no running test in the seam either.
        using (ExecutionContext.SuppressFlow()) {
            probe = Task.Run(
                async () => await Record.ExceptionAsync(() => Task.FromResult("x").Returns<Ok<string>>()),
                Xunit.TestContext.Current.CancellationToken);
        }

        var failure = Assert.IsType<InvalidOperationException>(await probe);

        Assert.Contains("needs a running test, and there is none", failure.Message);
    }

    /// <summary>An assembly that names no reader is told which attribute to declare.</summary>
    [Fact]
    public void AnAssemblyNamingNoReaderIsToldWhatToDeclare() {
        var failure = Assert.Throws<InvalidOperationException>(
            () => ClientAssertions.ReadersOf(typeof(string).Assembly, "Returns<Ok<String>>()"));

        Assert.Contains("System.Private.CoreLib names none", failure.Message);
        Assert.Contains("assembly attribute", failure.Message);
    }

    private static async Task Nothing() => await Task.Yield();

    private static Dictionary<string, string> Headers() => new(StringComparer.OrdinalIgnoreCase);
}
