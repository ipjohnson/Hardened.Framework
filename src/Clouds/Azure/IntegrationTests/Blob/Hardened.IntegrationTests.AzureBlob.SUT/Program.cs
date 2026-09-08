using System.Text.Json;
using Hardened.Azure.Functions.Runtime.Hosting;
using Hardened.IntegrationTests.AzureBlob.SUT;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// The blob fixture, hosted as a deployed function is; see the queue fixture's Program for the
// arrangement. The observed sink prints the marker the container harness reads.

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults(worker => worker.UseHardened<AzureBlobTestApp>())
    .ConfigureServices(services => services.AddSingleton<IUploadSink, ObservedUploadSink>())
    .Build();

host.Run();

internal sealed class ObservedUploadSink : IUploadSink {
    private const string Marker = "HARDENED-OBSERVED ";

    public void Arrived(Upload upload) {
        Console.Out.WriteLine(
            Marker + JsonSerializer.Serialize(
                new { kind = "blob", container = upload.Container, name = upload.Name, size = upload.Size }));
        Console.Out.Flush();
    }
}
