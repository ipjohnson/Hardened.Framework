using Hardened.Amz.Function.Lambda.Runtime.DependencyInjection;
using Hardened.Amz.Shared.Lambda.Runtime.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hardened.Amz.Function.Lambda.Runtime.Tests.DependencyInjection;

/// <summary>
/// A Lambda writes its logs to stdout for CloudWatch to pick up, so the module replaces whatever
/// logger providers were registered rather than adding to them. Leaving a console provider in place
/// duplicates every log line, and CloudWatch is billed by ingested byte.
/// </summary>
public class LambdaFunctionModuleTests {

    [Fact]
    public void TheLambdaLoggerProviderReplacesEveryOtherProvider() {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerProvider, StubLoggerProvider>();

        new LambdaFunctionModule().ConfigureServices(services);

        var provider = Assert.Single(services, d => d.ServiceType == typeof(ILoggerProvider));

        Assert.Equal(typeof(LambdaLoggerProvider), provider.ImplementationType);
    }

    [Fact]
    public void TheLambdaLoggerProviderIsRegisteredEvenWithNothingToReplace() {
        var services = new ServiceCollection();

        new LambdaFunctionModule().ConfigureServices(services);

        Assert.Single(services, d => d.ServiceType == typeof(ILoggerProvider));
    }

    [Fact]
    public void TheLoggerProviderIsASingletonSoTheStreamIsNotReopenedPerRequest() {
        var services = new ServiceCollection();

        new LambdaFunctionModule().ConfigureServices(services);

        var provider = Assert.Single(services, d => d.ServiceType == typeof(ILoggerProvider));

        Assert.Equal(ServiceLifetime.Singleton, provider.Lifetime);
    }

    private sealed class StubLoggerProvider : ILoggerProvider {
        public void Dispose() { }

        public ILogger CreateLogger(string categoryName) {
            throw new NotSupportedException("Registered only so the module has something to replace.");
        }
    }
}
