namespace Hardened.Shared.Runtime.Application;

public interface IApplicationModuleProvider {
    IEnumerable<IApplicationModule> ProvideModules();
}