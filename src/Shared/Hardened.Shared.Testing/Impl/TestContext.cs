using Hardened.Shared.Runtime.Diagnostics;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Hardened.Shared.Testing.Impl;

public class TestContext : ITestContext {
    public TestContext(CancellationToken cancellationRequest, ILogger logger) {
        CancellationRequest = cancellationRequest;
        Logger = logger;
        Retry = new RetryEngine(this);
    }

    public IRetryEngine Retry { get; }

    public CancellationToken CancellationRequest { get; }

    public ILogger Logger { get; }

    public void Step(Action step, string description, params object[] parameters) {
        bool status = false;

        var start = MachineTimestamp.Now;

        try {
            step();

            status = true;
        }
        finally {
            LogStep(status, start.GetElapsedMilliseconds(), description, parameters);
        }
    }


    public T Step<T>(Func<T> step, string description, params object[] parameters) {
        bool status = false;

        var start = MachineTimestamp.Now;

        try {
            var result = step();

            status = true;

            return result;
        }
        finally {
            LogStep(status, start.GetElapsedMilliseconds(), description, parameters);
        }
    }

    public async Task Step(Func<Task> step, string description, params object[] parameters) {
        bool status = false;

        var start = MachineTimestamp.Now;

        try {
            await step();

            status = true;
        }
        finally {
            LogStep(status, start.GetElapsedMilliseconds(), description, parameters);
        }
    }

    public async Task<T> Step<T>(Func<Task<T>> step, string description, params object[] parameters) {
        bool status = false;

        var start = MachineTimestamp.Now;

        try {
            var result = await step();

            status = true;

            return result;
        }
        finally {
            LogStep(status, start.GetElapsedMilliseconds(), description, parameters);
        }
    }

    private void LogStep(bool status, double duration, string description, object[] parameters) {
        var statusString = status ? "pass" : "fail";
        var logMessage = "{status} - " + description + " - {duration}ms";
        var parameterList = new List<object> { statusString };
        parameterList.AddRange(parameters);
        parameterList.Add(duration);

#pragma warning disable CA2254 // Template should be a static expression
        if (status) {
            Logger.LogInformation(logMessage, parameterList.ToArray());
        }
        else {
            Logger.LogError(logMessage, parameterList.ToArray());
        }
#pragma warning enable CA2254 // Template should be a static expression
    }
}