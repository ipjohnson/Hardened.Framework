using Hardened.Azure.Functions.Runtime.Hosting;
using Hardened.IntegrationTests.AzureQueue.SUT;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// The queue fixture, hosted as a deployed function is: the isolated worker the Functions host
// starts, speaking gRPC back to it. Nothing here knows whether the host is the emulator in a
// container or Azure - the same Program runs on both.
//
// The seam the host tier observes through is registered after the application, so a store the
// application might register itself is replaced rather than joined. A [Mock] cannot reach into
// another process; the store prints what it was given and the test reads the container's output.

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults(worker => worker.UseHardened<AzureQueueTestApp>())
    .ConfigureServices(services => services.AddSingleton<IOrderStore, ObservedOrderStore>())
    .Build();

host.Run();
