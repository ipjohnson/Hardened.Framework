using DependencyModules.Runtime.Attributes;

namespace Hardened.IntegrationTests.WebApp.SUT.Services;

/// <summary>
/// How many times each handler has actually run.
/// </summary>
/// <remarks>
/// A cache hit is only observable as the handler not running. Asserting on the response body alone
/// would pass against a framework that ran the handler again and happened to get the same answer,
/// which is exactly what a cache is supposed to avoid.
/// </remarks>
[SingletonService]
public class HandlerCallCounter {
    private readonly Dictionary<string, int> _calls = new(StringComparer.Ordinal);

    public int Next(string handler) {
        lock (_calls) {
            _calls.TryGetValue(handler, out var count);

            _calls[handler] = ++count;

            return count;
        }
    }
}
