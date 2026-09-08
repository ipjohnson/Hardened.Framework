using System.Text.Json;
using Hardened.Azure.Functions.Runtime.Hosting;
using Hardened.IntegrationTests.AzureChange.SUT;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// The change feed fixture, hosted as a deployed function is; see the queue fixture's Program for
// the arrangement. The observed projection prints the marker the container harness reads.

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults(worker => worker.UseHardened<AzureChangeTestApp>())
    .ConfigureServices(services => services.AddSingleton<IOrderProjection, ObservedProjection>())
    .Build();

host.Run();

internal sealed class ObservedProjection : IOrderProjection {
    private const string Marker = "HARDENED-OBSERVED ";

    public void Apply(Order order) => Print("orders", order);

    public void Audit(Order order) => Print("audit", order);

    private static void Print(string container, Order order) {
        Console.Out.WriteLine(
            Marker + JsonSerializer.Serialize(
                new { kind = "change", container, id = order.Id, quantity = order.Quantity }));
        Console.Out.Flush();
    }
}
