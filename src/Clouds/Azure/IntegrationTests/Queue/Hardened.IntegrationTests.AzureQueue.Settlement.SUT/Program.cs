using Hardened.Azure.Functions.Runtime.Hosting;
using Hardened.IntegrationTests.AzureQueue.SUT;
using Hardened.IntegrationTests.AzureQueue.Settlement.SUT;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// The queue fixture's Program with the settling application in place of the plain one; see the
// queue fixture for the arrangement.

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults(worker => worker.UseHardened<SettlementTestApp>())
    .ConfigureServices(services => services.AddSingleton<IOrderStore, ObservedOrderStore>())
    .Build();

host.Run();
