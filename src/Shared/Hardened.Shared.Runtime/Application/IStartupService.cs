namespace Hardened.Shared.Runtime.Application;

public interface IStartupService
{
    Task Startup(IServiceProvider rootProvider);
}