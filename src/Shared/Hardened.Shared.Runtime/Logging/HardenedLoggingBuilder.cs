using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.ComponentModel;

namespace Hardened.Shared.Runtime.Logging;

[DesignTimeVisible(false)]
public class HardenedLoggingBuilder : ILoggingBuilder {
    public HardenedLoggingBuilder(IServiceCollection services) {
        Services = services;
    }

    public IServiceCollection Services { get; }
}