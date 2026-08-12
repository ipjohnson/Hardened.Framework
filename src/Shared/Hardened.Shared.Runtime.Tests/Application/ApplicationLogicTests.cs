using System.Diagnostics;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Hardened.Shared.Runtime.Tests.Application;

/// <summary>
/// <see cref="ApplicationLogic"/> — the startup sequence every Hardened host runs before it does any
/// work.
///
/// <para>
/// The concurrency assertions here are gate-based rather than timing-based. Nothing sleeps waiting
/// for a race to resolve: a startup service returns a task the test controls, so "all of them were
/// started before any completed" is observable directly.
/// </para>
/// </summary>
public class ApplicationLogicTests {

    /// <summary>A startup service whose result the test decides, including when it decides it.</summary>
    private sealed class GatedStartupService(Func<IServiceProvider, Task<bool>> startup) : IStartupService {
        public Task<bool> Startup(IServiceProvider rootProvider) => startup(rootProvider);
    }

    private static IServiceProvider Provider(params IStartupService[] services) {
        var collection = new ServiceCollection();

        foreach (var service in services) {
            collection.AddSingleton(service);
        }

        return collection.BuildServiceProvider();
    }

    private static IStartupService Returning(bool result) =>
        new GatedStartupService(_ => Task.FromResult(result));

    [Fact]
    public async Task StartWithNothingToDoSucceeds() {
        Assert.Equal(0, await ApplicationLogic.Start(Provider(), null));
    }

    [Fact]
    public async Task StartSucceedsWhenEveryStartupServiceSucceeds() {
        Assert.Equal(0, await ApplicationLogic.Start(Provider(Returning(true), Returning(true)), null));
    }

    /// <summary>
    /// One service returning false fails startup. The host reads a non-zero result as "do not serve
    /// traffic", so this is the difference between a broken process exiting and a broken process
    /// answering requests.
    /// </summary>
    [Fact]
    public async Task StartFailsWhenAnyStartupServiceReturnsFalse() {
        Assert.Equal(1, await ApplicationLogic.Start(Provider(Returning(true), Returning(false)), null));
    }

    [Fact]
    public async Task StartFailsWhenEveryStartupServiceReturnsFalse() {
        Assert.Equal(1, await ApplicationLogic.Start(Provider(Returning(false), Returning(false)), null));
    }

    /// <summary>
    /// A startup service that throws surfaces the exception rather than being reported as a failed
    /// startup. The distinction matters: a false result is a decision, an exception is a defect, and
    /// flattening one into the other loses the stack trace.
    /// </summary>
    [Fact]
    public async Task AStartupServiceThatThrowsSurfacesItsException() {
        var provider = Provider(
            Returning(true),
            new GatedStartupService(_ => Task.FromException<bool>(new InvalidOperationException("boom"))));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ApplicationLogic.Start(provider, null));

        Assert.Equal("boom", exception.Message);
    }

    /// <summary>
    /// A service that throws before it returns a task aborts the sequence, since there is no task to
    /// collect. Worth pinning: it is the one case where the remaining services never start.
    /// </summary>
    [Fact]
    public async Task AStartupServiceThatThrowsSynchronouslySurfacesItsException() {
        var laterServiceStarted = false;

        var provider = Provider(
            new GatedStartupService(_ => throw new InvalidOperationException("boom")),
            new GatedStartupService(_ => {
                laterServiceStarted = true;
                return Task.FromResult(true);
            }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => ApplicationLogic.Start(provider, null));

        Assert.False(laterServiceStarted);
    }

    /// <summary>
    /// Every startup service is started before any is awaited, so slow independent work overlaps
    /// instead of queueing. A sequential implementation would leave the second service unstarted
    /// while the first is still pending.
    /// </summary>
    [Fact]
    public async Task EveryStartupServiceIsStartedBeforeAnyIsAwaited() {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;

        var provider = Provider(
            Enumerable.Range(0, 3)
                .Select(_ => (IStartupService)new GatedStartupService(_ => {
                    Interlocked.Increment(ref started);
                    return gate.Task;
                }))
                .ToArray());

        var startup = ApplicationLogic.Start(provider, null);

        Assert.Equal(3, started);
        Assert.False(startup.IsCompleted);

        gate.SetResult(true);

        Assert.Equal(0, await startup);
    }

    [Fact]
    public async Task TheStartupTaskArgumentRunsAlongsideTheStartupServices() {
        var taskRan = false;

        var result = await ApplicationLogic.Start(Provider(Returning(true)), _ => {
            taskRan = true;
            return Task.FromResult(true);
        });

        Assert.Equal(0, result);
        Assert.True(taskRan);
    }

    [Fact]
    public async Task AFailingStartupTaskArgumentFailsStartup() {
        Assert.Equal(1, await ApplicationLogic.Start(Provider(Returning(true)), _ => Task.FromResult(false)));
    }

    /// <summary>The startup task is given the root provider, not a scope of it.</summary>
    [Fact]
    public async Task TheStartupTaskArgumentReceivesTheRootProvider() {
        var provider = Provider();
        IServiceProvider? received = null;

        await ApplicationLogic.Start(provider, serviceProvider => {
            received = serviceProvider;
            return Task.FromResult(true);
        });

        Assert.Same(provider, received);
    }

    [Fact]
    public async Task EachStartupServiceReceivesTheRootProvider() {
        var provider = Provider();
        IServiceProvider? received = null;

        var withService = Provider(new GatedStartupService(serviceProvider => {
            received = serviceProvider;
            return Task.FromResult(true);
        }));

        await ApplicationLogic.Start(withService, null);

        Assert.Same(withService, received);
        Assert.NotSame(provider, received);
    }

    [Fact]
    public void StartWithWaitRunsStartupToCompletionWhenItFinishesInTime() {
        var completed = false;

        ApplicationLogic.StartWithWait(
            Provider(new GatedStartupService(_ => {
                completed = true;
                return Task.FromResult(true);
            })),
            null,
            timeoutInSeconds: 30);

        Assert.True(completed);
    }

    /// <summary>
    /// Startup that does not finish in time is abandoned rather than waited on forever. The host
    /// carries on with an incomplete startup, which is the behaviour — not an opinion about whether
    /// it is the right one.
    /// </summary>
    [Fact]
    public void StartWithWaitGivesUpOnStartupThatOverrunsItsTimeout() {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = Provider(new GatedStartupService(_ => gate.Task));

        var stopwatch = Stopwatch.StartNew();

        ApplicationLogic.StartWithWait(provider, null, timeoutInSeconds: 1);

        stopwatch.Stop();

        Assert.False(gate.Task.IsCompleted);
        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(500),
            $"StartWithWait returned after {stopwatch.ElapsedMilliseconds}ms, so it did not wait for its timeout.");

        gate.SetResult(true);
    }

    /// <summary>
    /// A startup failure is not swallowed by the wait. <c>Task.Wait</c> wraps it, which is what a
    /// host sees.
    /// </summary>
    [Fact]
    public void StartWithWaitSurfacesAStartupException() {
        var provider = Provider(
            new GatedStartupService(_ => Task.FromException<bool>(new InvalidOperationException("boom"))));

        var exception = Assert.Throws<AggregateException>(
            () => ApplicationLogic.StartWithWait(provider, null, timeoutInSeconds: 30));

        Assert.IsType<InvalidOperationException>(exception.Flatten().InnerException);
    }

    private static IServiceProvider ProviderWithDelegate(
        Func<Task<int>> applicationDelegate,
        bool shouldStartApp,
        params IStartupService[] services) {

        var delegateProvider = Substitute.For<IApplicationDelegateProvider>();

        delegateProvider
            .ProvideDelegate(Arg.Any<IHardenedEnvironment>(), Arg.Any<IServiceProvider>())
            .Returns(Task.FromResult(new ApplicationDelegate(applicationDelegate, shouldStartApp)));

        var collection = new ServiceCollection();

        collection.AddSingleton(delegateProvider);

        foreach (var service in services) {
            collection.AddSingleton(service);
        }

        return collection.BuildServiceProvider();
    }

    [Fact]
    public async Task RunApplicationReturnsWhatTheApplicationDelegateReturns() {
        var provider = ProviderWithDelegate(() => Task.FromResult(7), shouldStartApp: true);

        Assert.Equal(7, await ApplicationLogic.RunApplication(new EnvironmentImpl("test"), provider, null));
    }

    [Fact]
    public async Task RunApplicationRunsStartupBeforeTheApplicationDelegate() {
        var order = new List<string>();

        var provider = ProviderWithDelegate(
            () => {
                order.Add("delegate");
                return Task.FromResult(0);
            },
            shouldStartApp: true,
            new GatedStartupService(_ => {
                order.Add("startup");
                return Task.FromResult(true);
            }));

        await ApplicationLogic.RunApplication(new EnvironmentImpl("test"), provider, null);

        Assert.Equal(["startup", "delegate"], order);
    }

    /// <summary>
    /// A delegate that says it does not need the application started skips startup entirely. This is
    /// how a command that only prints help avoids paying for the whole application.
    /// </summary>
    [Fact]
    public async Task RunApplicationSkipsStartupWhenTheDelegateDoesNotWantIt() {
        var startupRan = false;

        var provider = ProviderWithDelegate(
            () => Task.FromResult(0),
            shouldStartApp: false,
            new GatedStartupService(_ => {
                startupRan = true;
                return Task.FromResult(true);
            }));

        await ApplicationLogic.RunApplication(new EnvironmentImpl("test"), provider, null);

        Assert.False(startupRan);
    }

    /// <summary>
    /// Failed startup stops the application before its delegate runs, and the failure is what the
    /// process exits with. Running the delegate anyway is how a half-initialised process ends up
    /// serving traffic.
    /// </summary>
    [Fact]
    public async Task RunApplicationDoesNotRunTheDelegateWhenStartupFails() {
        var delegateRan = false;

        var provider = ProviderWithDelegate(
            () => {
                delegateRan = true;
                return Task.FromResult(0);
            },
            shouldStartApp: true,
            Returning(false));

        var result = await ApplicationLogic.RunApplication(new EnvironmentImpl("test"), provider, null);

        Assert.Equal(1, result);
        Assert.False(delegateRan);
    }

    [Fact]
    public async Task RunApplicationPassesTheEnvironmentToTheDelegateProvider() {
        var environment = new EnvironmentImpl("staging");
        var delegateProvider = Substitute.For<IApplicationDelegateProvider>();

        delegateProvider
            .ProvideDelegate(Arg.Any<IHardenedEnvironment>(), Arg.Any<IServiceProvider>())
            .Returns(Task.FromResult(new ApplicationDelegate(() => Task.FromResult(0), false)));

        var provider = new ServiceCollection()
            .AddSingleton(delegateProvider)
            .BuildServiceProvider();

        await ApplicationLogic.RunApplication(environment, provider, null);

        await delegateProvider.Received(1).ProvideDelegate(environment, provider);
    }

    /// <summary>
    /// Without an <see cref="IApplicationDelegateProvider"/> there is no application to run, and the
    /// resolution failure says so rather than the host starting and doing nothing.
    /// </summary>
    [Fact]
    public async Task RunApplicationWithNoDelegateProviderThrows() {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ApplicationLogic.RunApplication(
                new EnvironmentImpl("test"), new ServiceCollection().BuildServiceProvider(), null));
    }
}
