namespace Hardened.Shared.Runtime.Application;

public interface IStartupService {
    Task<bool> Startup(IServiceProvider rootProvider);
}