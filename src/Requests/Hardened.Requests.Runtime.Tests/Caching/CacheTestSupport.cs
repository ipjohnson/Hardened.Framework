using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Caching;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Execution;

namespace Hardened.Requests.Runtime.Tests.Caching;

/// <summary>
/// The pieces every caching test needs: a handler to attach declarations to, a store that records
/// what it was asked, and key providers that answer predictably.
/// </summary>
/// <remarks>
/// Every provider here is stateless. A provider a test could reconfigure would have to be
/// reconfigured through a static, because the attribute reaches <c>Create</c> through a generic
/// constraint and leaves no constructor to pass anything to - and a mutable static is shared across
/// the test classes xUnit runs in parallel.
/// </remarks>
internal static class CacheTestSupport {

    private class Controller { }

    public static ExecutionRequestHandlerInfo Handler(
        object[] metadata,
        string path = "/catalog",
        string method = "GET",
        Requirement? requirement = null) =>
        new(path, method, typeof(Controller), "Browse", metadata: metadata, requirement: requirement);

    /// <summary>
    /// A store that keeps what it was given and records what it was asked.
    /// </summary>
    public sealed class RecordingStore : IResponseCacheStore {
        private readonly Dictionary<string, CachedResponse> _entries = new(StringComparer.Ordinal);

        public List<string> Reads { get; } = [];

        public List<(string Key, TimeSpan Duration)> Writes { get; } = [];

        public ValueTask<CachedResponse?> Get(string key, CancellationToken cancellationToken) {
            Reads.Add(key);

            return new ValueTask<CachedResponse?>(
                _entries.TryGetValue(key, out var entry) ? entry : null);
        }

        public ValueTask Set(
            string key, CachedResponse response, TimeSpan duration, CancellationToken cancellationToken) {
            Writes.Add((key, duration));
            _entries[key] = response;

            return default;
        }
    }

    /// <summary>
    /// Answers whatever the test's delegate does, so a filter test can key on anything.
    /// </summary>
    public sealed class Keyed : ICacheKeyProvider {
        private readonly Func<IExecutionContext, string?> _key;

        public Keyed(Func<IExecutionContext, string?> key) {
            _key = key;
        }

        public static ICacheKeyProvider Create(string[] values) => new Keyed(_ => "keyed");

        public ValueTask<string?> Key(IExecutionContext context) => new(_key(context));
    }

    /// <summary>Answers the same key for every request.</summary>
    public sealed class FixedKey : ICacheKeyProvider {
        public static ICacheKeyProvider Create(string[] values) => new FixedKey();

        public ValueTask<string?> Key(IExecutionContext context) => new("fixed");
    }

    /// <summary>A second strategy, so composition has two distinguishable parts.</summary>
    public sealed class SecondKey : ICacheKeyProvider {
        public static ICacheKeyProvider Create(string[] values) => new SecondKey();

        public ValueTask<string?> Key(IExecutionContext context) => new("second");
    }

    /// <summary>Refuses to be built, so a test can assert the failure names the handler.</summary>
    public sealed class Unbuildable : ICacheKeyProvider {
        public static ICacheKeyProvider Create(string[] values) =>
            throw new ArgumentException("Unbuildable takes no values.", nameof(values));

        public ValueTask<string?> Key(IExecutionContext context) => new("never");
    }
}
