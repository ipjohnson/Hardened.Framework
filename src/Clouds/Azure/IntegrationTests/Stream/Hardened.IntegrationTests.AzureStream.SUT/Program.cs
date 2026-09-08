using Hardened.Azure.Functions.Runtime.Hosting;
using Hardened.IntegrationTests.AzureStream.SUT;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// The stream fixture, hosted as a deployed function is; see the queue fixture's Program for the
// arrangement, and for why the observed sink is registered here rather than in the application.

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults(worker => worker.UseHardened<AzureStreamTestApp>())
    .ConfigureServices(services => services.AddSingleton<IClickSink, ObservedClickSink>())
    .Build();

host.Run();
