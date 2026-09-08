using Hardened.Azure.Functions.Runtime.Hosting;
using Hardened.IntegrationTests.AzureEvents.SUT;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// The events fixture, hosted as a deployed function app is; see the queue fixture's Program for
// the arrangement. The observed log prints the marker the container harness reads.

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults(worker => worker.UseHardened<AzureEventsTestApp>())
    .ConfigureServices(services => services.AddSingleton<ITriggerLog, ObservedTriggerLog>())
    .Build();

host.Run();

internal sealed class ObservedTriggerLog : ITriggerLog {
    private const string Marker = "HARDENED-OBSERVED ";

    public void Record(string entry) {
        var separator = entry.IndexOf(':');

        Console.Out.WriteLine(
            Marker + System.Text.Json.JsonSerializer.Serialize(new {
                kind = separator < 0 ? entry : entry.Substring(0, separator),
                id = separator < 0 ? "" : entry.Substring(separator + 1)
            }));
        Console.Out.Flush();
    }
}
