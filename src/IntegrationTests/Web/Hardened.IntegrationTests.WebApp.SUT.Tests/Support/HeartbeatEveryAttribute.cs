using System.Reflection;
using Hardened.Requests.Runtime.Streaming;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Support;

/// <summary>
/// Sets the heartbeat interval for one test's application.
/// </summary>
/// <remarks>
/// Per test rather than on the application, because the interval is global and the event-stream
/// tests assert exact bodies: a short interval on every test would put a heartbeat into whichever
/// of them happened to yield slowly on a loaded runner. Registered after the module, so it amends
/// what the request module registered.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class HeartbeatEveryAttribute : Attribute, IHardenedTestDependencyRegistrationAttribute {
    private readonly int _milliseconds;

    public HeartbeatEveryAttribute(int milliseconds) {
        _milliseconds = milliseconds;
    }

    public void RegisterDependencies(
        AttributeCollection attributeCollection,
        MethodInfo methodInfo,
        IHardenedEnvironment environment,
        IServiceCollection serviceCollection) {
        serviceCollection.ConfigureStreaming(
            streaming => streaming.HeartbeatInterval = TimeSpan.FromMilliseconds(_milliseconds));
    }
}
