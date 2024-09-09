using Hardened.Shared.Runtime.Application;
using Hardened.Templates.Abstract;
using Hardened.Templates.Runtime.Helpers;
using Hardened.Templates.Runtime.Impl;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hardened.Templates.Runtime.DependencyInjection;

public static class TemplateDI {
    private static readonly WeakReference<IServiceCollection> _lastServiceCollection = new(default!);

    public static void Register(IHardenedEnvironment environment, IServiceCollection serviceCollection) {
        if (!_lastServiceCollection.TryGetTarget(out var lastServiceCollection) ||
            !ReferenceEquals(lastServiceCollection, serviceCollection)) {
            _lastServiceCollection.SetTarget(serviceCollection);

            serviceCollection.TryAddSingleton<IBooleanLogicService, BooleanLogicService>();
            serviceCollection.TryAddSingleton<IDataFormattingService, DataFormattingService>();
            serviceCollection.TryAddSingleton<ITemplateExecutionService, TemplateExecutionService>();
            serviceCollection.TryAddSingleton<ITemplateHelperService, TemplateHelperService>();
            serviceCollection.TryAddSingleton<IStringEscapeServiceProvider, StringEscapeServiceProvider>();
            serviceCollection.AddSingleton<IStringEscapeService, NoopEscapeStringService>();
            serviceCollection.AddSingleton<IStringEscapeService, HtmlEscapeStringService>();
            serviceCollection.TryAddSingleton<IInternalTemplateServices, InternalTemplateServices>();
            serviceCollection.TryAddSingleton<ITemplateHelperProvider, DefaultHelpers>();
        }
    }
}