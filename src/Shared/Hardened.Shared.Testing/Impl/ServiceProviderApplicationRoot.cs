using Hardened.Shared.Runtime.Application;

namespace Hardened.Shared.Testing.Impl;

public class ServiceProviderApplicationRoot : IApplicationRoot {
    public IServiceProvider Provider { get; }

    public ServiceProviderApplicationRoot(IServiceProvider sp) => Provider = sp;

    public ValueTask DisposeAsync() => default;
}
