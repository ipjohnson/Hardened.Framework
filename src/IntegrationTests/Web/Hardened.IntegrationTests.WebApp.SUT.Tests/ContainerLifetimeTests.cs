using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using DependencyModules.Testing.Attributes.Interfaces;
using Hardened.IntegrationTests.WebApp.SUT.Tests;
using Hardened.Web.Kestrel.Runtime;

[assembly: TrackContainers]
[assembly: AssemblyFixture(typeof(ContainerLeakGuard))]

namespace Hardened.IntegrationTests.WebApp.SUT.Tests;

/// <summary>
/// A test's container is disposed when its test has run, not when the run ends.
/// </summary>
/// <remarks>
/// <para>
/// Until DependencyModules 1.3.1 it was not, under xUnit: <c>ModuleTestCase</c> handed the
/// provider to the case's <c>DisposalTracker</c>, and xUnit disposes a test case only once every
/// case in the assembly has run. A probe on 2026-09-05 that handed three providers to an assembly
/// fixture found all three alive at the end of the run. With a socket host that would have been
/// a listener per test held open until the last test.
/// </para>
/// <para>
/// Two tests in one class, which xUnit runs one after the other in an order it does not promise:
/// whichever runs second asserts the first's container has been disposed, which it observes as
/// the <see cref="ObjectDisposedException"/> a disposed provider answers with. The socket pair
/// does the same over the port: the second connects to the first's, and is refused.
/// </para>
/// </remarks>
public class ContainerLifetimeTests {

    private static readonly ConcurrentBag<IServiceProvider> Earlier = new();

    [HardenedTest]
    public void TheContainerOfATestThatHasRunIsDisposed(IServiceProvider provider) => Check(provider);

    [HardenedTest]
    public void WhicheverOfTheTwoRanFirst(IServiceProvider provider) => Check(provider);

    private static void Check(IServiceProvider current) {
        foreach (var earlier in Earlier) {
            Assert.Throws<ObjectDisposedException>(() => earlier.GetService(typeof(object)));
        }

        Assert.Null(current.GetService(typeof(ContainerLifetimeTests)));

        Earlier.Add(current);
    }
}

/// <summary>The socket half: a finished test's port is closed by the time the next test runs.</summary>
[KestrelRuntime]
public class SocketLifetimeTests {

    private static readonly ConcurrentBag<int> EarlierPorts = new();

    [HardenedTest]
    public async Task ThePortOfATestThatHasRunIsClosed(ITestWebApp app) => await Check(app);

    [HardenedTest]
    public async Task WhicheverOfTheTwoRanFirst(ITestWebApp app) => await Check(app);

    private static async Task Check(ITestWebApp app) {
        var token = TestContext.Current.CancellationToken;

        foreach (var port in EarlierPorts) {
            using var probe = new TcpClient();

            await Assert.ThrowsAsync<SocketException>(() => probe.ConnectAsync(IPAddress.Loopback, port, token).AsTask());
        }

        var response = await app.Get("/verbs/item/1");

        response.Assert.Ok();

        EarlierPorts.Add(new Uri(app.CreateHttpClient().BaseAddress!.ToString()).Port);
    }
}

/// <summary>Hands every test's container to <see cref="ContainerLeakGuard"/>.</summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class TrackContainersAttribute : Attribute, ITestStartupAttribute {

    public Task StartupAsync(ITestMethodContext testMethod, IServiceProvider serviceProvider) {
        ContainerLeakGuard.Track(serviceProvider);

        return Task.CompletedTask;
    }
}

/// <summary>
/// At the end of the run, every container a test built has been disposed. The probe that found
/// the defect, turned into a guard over the whole assembly rather than one class's pair.
/// </summary>
public sealed class ContainerLeakGuard : IAsyncDisposable {

    private static readonly ConcurrentBag<IServiceProvider> Containers = new();

    public static void Track(IServiceProvider provider) => Containers.Add(provider);

    public ValueTask DisposeAsync() {
        var alive = 0;

        foreach (var container in Containers) {
            try {
                container.GetService(typeof(object));
                alive++;
            }
            catch (ObjectDisposedException) {
            }
        }

        if (alive > 0) {
            throw new InvalidOperationException(
                $"{alive} of {Containers.Count} test containers were still alive at the end of the run. " +
                "The runner disposes a container when its case has run; something has stopped doing so.");
        }

        return default;
    }
}
