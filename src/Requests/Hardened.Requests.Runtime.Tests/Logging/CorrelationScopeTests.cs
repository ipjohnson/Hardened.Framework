using Hardened.Requests.Runtime.Logging;
using Hardened.Requests.Runtime.Tests.Support;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Logging;

/// <summary>
/// What the logger hands a provider so that every line written while a request runs carries its
/// correlation id.
/// </summary>
/// <remarks>
/// A provider reads the scope as <c>IReadOnlyList&lt;KeyValuePair&lt;string, object&gt;&gt;</c>, and
/// which spelling it reads it through is the provider's decision: the console formatter enumerates,
/// the JSON one indexes, and a provider with no structure to put it in takes <c>ToString</c>. All
/// three have to say the same thing, and the suite's own capturing logger discards the state, so
/// none of them was read anywhere.
/// </remarks>
public class CorrelationScopeTests
{
    private sealed class ScopeCapturing : ILogger<RequestLogger>
    {
        public List<object> Scopes { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            Scopes.Add(state);

            return new Closing();
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) { }

        private sealed class Closing : IDisposable
        {
            public void Dispose() { }
        }
    }

    private static IReadOnlyList<KeyValuePair<string, object>> Scope(out string correlationId)
    {
        var logger = new ScopeCapturing();
        var context = Pipeline.Context();

        correlationId = context.CorrelationId;

        new RequestLogger(logger).RequestBegin(context);

        return Assert.IsAssignableFrom<IReadOnlyList<KeyValuePair<string, object>>>(
            Assert.Single(logger.Scopes)
        );
    }

    [Fact]
    public void TheScopeIsTheOneCorrelationIdTheContextIssued()
    {
        var scope = Scope(out var correlationId);

        Assert.Single(scope);
        Assert.Equal("CorrelationId", scope[0].Key);
        Assert.Equal(correlationId, scope[0].Value);
    }

    [Fact]
    public void EnumeratingReadsTheSamePairIndexingDoes()
    {
        var scope = Scope(out var correlationId);

        Assert.Equal(
            [new KeyValuePair<string, object>("CorrelationId", correlationId)],
            scope.ToList()
        );
    }

    /// <summary>
    /// One pair, so anything past it is the caller's mistake rather than an empty answer.
    /// </summary>
    [Fact]
    public void IndexingPastTheOnePairThrows()
    {
        var scope = Scope(out _);

        Assert.Throws<ArgumentOutOfRangeException>(() => scope[1]);
    }

    [Fact]
    public void ToStringNamesTheKeyAndTheValue()
    {
        var scope = Scope(out var correlationId);

        Assert.Equal("CorrelationId:" + correlationId, scope.ToString());
    }
}
