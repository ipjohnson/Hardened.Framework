using Microsoft.Extensions.Logging;

namespace Hardened.Shared.Testing;

public interface ITestContext
{
    CancellationToken CancellationRequest { get; }

    void Step(Action step, string description, params object[] parameters);

    T Step<T>(Func<T> step, string description, params object[] parameters);
    
    Task Step(Func<Task> step, string description, params object[] parameters);

    Task<T> Step<T>(Func<Task<T>> step, string description, params object[] parameters);
    
    ILogger Logger { get; }
}