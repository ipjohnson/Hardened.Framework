using Hardened.Gcp.Functions.Runtime.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Gcp.Functions.Runtime.Tests.Hosting;

/// <summary>
/// A process as the Functions Framework would have built it, driven through the real startup.
/// </summary>
/// <remarks>
/// <para>
/// The two calls in the order the framework makes them: <c>ConfigureServices</c> while the host is
/// being built, then <c>Configure</c> during pipeline configuration, which runs inside
/// <c>GenericWebHostService.StartAsync</c> and before the server listens. Nothing here stands in
/// for either of them, so a startup that registered the wrong thing fails the tests below rather
/// than being described by them.
/// </para>
/// <para>
/// What is not reproduced is the resolution of <c>FUNCTION_TARGET</c> and the ASP.NET Core
/// pipeline in front of it. <see cref="CloudFunctionEntryPointTests"/> covers the first against the
/// framework's own public API; the second is Google's code and is what the deployed benchmark
/// measures.
/// </para>
/// </remarks>
internal sealed class CloudFunctionFixture : IDisposable
{
    private readonly ServiceProvider _provider;

    public CloudFunctionFixture(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        var startup = new HardenedFunctionsStartup<FunctionsApp>();

        startup.ConfigureServices(new WebHostBuilderContext(), services);

        configure?.Invoke(services);

        _provider = services.BuildServiceProvider();

        startup.Configure(new WebHostBuilderContext(), new ApplicationBuilder(_provider));

        Host = _provider.GetRequiredService<CloudFunctionHost>();
    }

    public IServiceProvider Provider => _provider;

    public CloudFunctionHost Host { get; }

    /// <summary>
    /// One delivery, in its own scope, the way ASP.NET Core gives each request one.
    /// </summary>
    public async Task<HttpContext> Deliver(Func<IServiceProvider, DefaultHttpContext> request)
    {
        using var scope = _provider.CreateScope();

        var context = request(scope.ServiceProvider);

        await Host.HandleAsync(context);

        return context;
    }

    public void Dispose() => _provider.Dispose();
}

/// <summary>Records what the queue handler was given.</summary>
internal sealed class RecordingOrderStore : IOrderStore
{
    private readonly List<Order> _placed = new();

    public IReadOnlyList<Order> Placed => _placed;

    public void Place(Order order) => _placed.Add(order);
}
