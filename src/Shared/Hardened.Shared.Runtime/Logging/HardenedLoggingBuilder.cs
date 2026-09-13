using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hardened.Shared.Runtime.Logging;

[DesignTimeVisible(false)]
public class HardenedLoggingBuilder : ILoggingBuilder
{
    public HardenedLoggingBuilder(IServiceCollection services)
    {
        Services = services;
    }

    public IServiceCollection Services { get; }
}
