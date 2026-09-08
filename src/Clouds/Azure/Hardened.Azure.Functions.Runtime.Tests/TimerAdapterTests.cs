using Hardened.Azure.Functions.Runtime.Execution;
using Hardened.Azure.Functions.Testing;
using Hardened.Azure.Functions.Timer;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.Azure.Functions.Runtime.Tests;

public class TimerAdapterTests {
    private static readonly TimerAdapter Adapter = new();

    private static TestFunctionContext Context() =>
        new("Timer_nightly", new Dictionary<string, object?>(), new ServiceCollection().BuildServiceProvider());

    /// <summary>
    /// Three families bind a string, so the type alone says nothing; the scheme decides.
    /// </summary>
    [Fact]
    public void HandlesAStringOnlyUnderTheTimerScheme() {
        Assert.True(Adapter.Handles(new FunctionsTrigger("TIMER", "/nightly", "{}")));
        Assert.False(Adapter.Handles(new FunctionsTrigger("CHANGE", "/orders", "[]")));
        Assert.False(Adapter.Handles(new FunctionsTrigger("TIMER", "/nightly", new object())));
    }

    [Fact]
    public void TheRouteIsTheShimsAndTheBodyIsTheTimersJson() {
        const string timer = """{"Schedule":{"AdjustForDST":true},"IsPastDue":false}""";

        var request = Adapter.CreateRequest(new FunctionsTrigger("TIMER", "/nightly", timer), Context());

        Assert.Equal("TIMER", request.Method);
        Assert.Equal("/nightly", request.Path);
        Assert.Equal("application/json", request.ContentType);
        Assert.Equal(timer, new StreamReader(request.Body).ReadToEnd());
    }

    /// <summary>
    /// The one fact lifted into a header, so a handler skipping late runs need not bind the body.
    /// </summary>
    [Theory]
    [InlineData("""{"IsPastDue":true}""", "true")]
    [InlineData("""{"isPastDue":false}""", "false")]
    public void WhetherTheRunIsLateIsAHeader(string timer, string expected) {
        var request = Adapter.CreateRequest(new FunctionsTrigger("TIMER", "/nightly", timer), Context());

        Assert.Equal(expected, request.Headers[TimerAdapter.PastDueHeader].ToString());
    }

    [Fact]
    public void AnEmptyTimerHasNoBodyAndNoPastDueHeader() {
        var request = Adapter.CreateRequest(new FunctionsTrigger("TIMER", "/nightly", ""), Context());

        Assert.Same(Stream.Null, request.Body);
        Assert.False(request.Headers.ContainsKey(TimerAdapter.PastDueHeader));
    }
}
