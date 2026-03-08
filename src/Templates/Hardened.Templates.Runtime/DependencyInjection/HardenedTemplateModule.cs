using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Templates.Abstract;
using Hardened.Templates.Runtime.Helpers;
using Hardened.Templates.Runtime.Impl;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hardened.Templates.Runtime.DependencyInjection;

[DependencyModule]
public partial class HardenedTemplateModule : IServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services) {
        services.TryAddSingleton<IBooleanLogicService, BooleanLogicService>();
        services.TryAddSingleton<IDataFormattingService, DataFormattingService>();
        services.TryAddSingleton<ITemplateExecutionService, TemplateExecutionService>();
        services.TryAddSingleton<ITemplateHelperService, TemplateHelperService>();
        services.TryAddSingleton<IStringEscapeServiceProvider, StringEscapeServiceProvider>();
        services.AddSingleton<IStringEscapeService, NoopEscapeStringService>();
        services.AddSingleton<IStringEscapeService, HtmlEscapeStringService>();
        services.TryAddSingleton<IInternalTemplateServices, InternalTemplateServices>();
        services.TryAddSingleton<ITemplateHelperProvider, DefaultHelpers>();
    }
}
