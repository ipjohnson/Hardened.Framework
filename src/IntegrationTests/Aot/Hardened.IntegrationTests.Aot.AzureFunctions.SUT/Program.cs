using System.Text.Json.Serialization.Metadata;
using Hardened.Azure.Functions.Runtime.Hosting;
using Hardened.IntegrationTests.Aot.AzureFunctions.SUT;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// A Hardened Azure Functions worker published with Native AOT. The Kestrel sibling proves a native
// binary can serve a web route, the Lambda sibling that one can serve an invocation, and the Cloud
// Run sibling both on one socket; this one is both behind the Functions host, which starts the
// binary, indexes it through the generated metadata provider and invokes it over gRPC. The CI
// probe runs that host, reads the functions it indexed and asks for the web route through it.
//
// The same Program.cs a deployed worker has: the worker defaults, and Hardened's registration.

var host = new HostBuilder()
    // Registered ahead of the application, so what AotSerializerModule resolves models through is
    // in place before the serializers are. Without it they throw rather than falling back to
    // reflection, which is the difference this project exists to hold.
    .ConfigureServices(services => {
        services.AddSingleton<IJsonTypeInfoResolver>(AotContext.Default);
        services.AddSingleton<IOrderSink, OrderSink>();
    })
    .ConfigureFunctionsWorkerDefaults(worker => worker.UseHardened<Application>())
    .Build();

host.Run();
