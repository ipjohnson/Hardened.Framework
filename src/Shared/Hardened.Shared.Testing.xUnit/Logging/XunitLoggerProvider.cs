using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Hardened.Shared.Testing.Logging;

public class XunitLoggerProvider : ILoggerProvider {
    private readonly ConcurrentDictionary<string, XUnitLogger> _loggers = new();

    public void Dispose() { }

    public ILogger CreateLogger(string categoryName) {
        return _loggers.GetOrAdd(
            categoryName,
            s => new XUnitLogger(s));
    }
}
