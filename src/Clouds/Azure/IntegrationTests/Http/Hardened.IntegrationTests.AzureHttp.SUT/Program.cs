using Hardened.Azure.Functions.Runtime.Hosting;
using Hardened.IntegrationTests.AzureHttp.SUT;
using Microsoft.Extensions.Hosting;

// The HTTP fixture, hosted as a deployed function is: the isolated worker the Functions host
// starts, speaking gRPC back to it. ConfigureFunctionsWorkerDefaults and not
// ConfigureFunctionsWebApplication: the request reaches the worker as HttpRequestData over the
// worker channel, and no ASP.NET Core server is started in front of the application.

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults(worker => worker.UseHardened<AzureHttpTestApp>())
    .Build();

host.Run();
