using Hardened.Commands;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.IntegrationTests.Console.SUT;

[HardenedModule]
public partial class Application {
    
    private IEnumerable<IApplicationModule> Modules() {
        yield return new CommandsLibrary();
    }

    public void RegisterDependencies(IServiceCollection collection) {
        
    }
}