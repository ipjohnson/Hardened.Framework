namespace Hardened.Shared.Runtime.Application;

public interface IApplicationRoot : IAsyncDisposable
{
    IServiceProvider Provider { get; }
}