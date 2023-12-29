using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hardened.IntegrationTests.Console.SUT;

public partial class Application : IApplicationRoot {
    private readonly IEnvironment _environment;
    private ServiceProvider? _rootProvider;

    public Application() : this(new EnvironmentImpl()) { }

    public Application(IEnvironment environment) {
        this._environment = environment;

        Action<ILoggingBuilder> loggingBuilderAction = builder => { };
        _rootProvider = CreateServiceProvider(_environment, null, loggingBuilderAction, null);
    }

    public static async Task<int> Main(string[] args) {
        var application = new Application(new EnvironmentImpl(arguments: args));

        var returnValue = await application.Run();

        await application.DisposeAsync();

        return returnValue;
    }

    private Task<int> Run() {
        if (_rootProvider == null) {
            throw new Exception("Root providers should not be null at this point");
        }

        return ApplicationLogic.RunApplication(_environment, _rootProvider, null);
    }

    public IServiceProvider Provider => _rootProvider ?? throw new Exception("Root provider is null");

    public async ValueTask DisposeAsync() {
        if (_rootProvider is IAsyncDisposable container) {
            _rootProvider = null;
            await container.DisposeAsync();
        }
    }
}