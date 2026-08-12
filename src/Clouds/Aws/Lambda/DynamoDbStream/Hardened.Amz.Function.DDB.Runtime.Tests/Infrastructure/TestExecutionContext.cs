using System.Text;
using Hardened.Amz.Function.Lambda.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Headers;
using Hardened.Shared.Runtime.Diagnostics;
using Hardened.Shared.Runtime.Metrics;
using Microsoft.Extensions.Primitives;
using NSubstitute;

namespace Hardened.Amz.Function.DDB.Runtime.Tests.Infrastructure;

/// <summary>
/// Builds the real <see cref="LambdaExecutionContext"/> the runtime builds, so the filters under
/// test see production types rather than a substitute that agrees with the test.
/// </summary>
public static class TestExecutionContext {
    public static LambdaExecutionContext Create(
        Stream requestBody,
        Stream responseBody,
        IServiceProvider? requestServices = null,
        string method = "Invoke",
        string path = "TestFunction") {
        var services = requestServices ?? new StubServiceProvider();

        var request = new LambdaExecutionRequest(
            method, path, requestBody, new Dictionary<string, StringValues>());

        var response = new LambdaExecutionResponse(responseBody, new HeaderCollectionStringValues());

        return new LambdaExecutionContext(
            services,
            services,
            Substitute.For<IKnownServices>(),
            request,
            response,
            new NullMetricsLogger(),
            MachineTimestamp.Now);
    }

    public static string ReadAll(Stream stream) {
        stream.Position = 0;

        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);

        return reader.ReadToEnd();
    }
}

/// <summary>
/// A service provider backed by a dictionary. Enough for the handful of services the filters
/// resolve, without pulling a container into a unit test.
/// </summary>
public sealed class StubServiceProvider : IServiceProvider {
    private readonly Dictionary<Type, object> _services = new();

    public StubServiceProvider Add<T>(T service) where T : notnull {
        _services[typeof(T)] = service;

        return this;
    }

    public object? GetService(Type serviceType) {
        return _services.GetValueOrDefault(serviceType);
    }
}
