using System;
using System.Collections.Generic;
using System.Text;
using Hardened.Templates.Abstract;
using Hardened.Templates.Runtime.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hardened.Templates.Runtime.Impl
{
    public static class DependencyInjectionRegistration
    {
        public static IServiceCollection RegisterKnownServices(IServiceCollection serviceCollection)
        {
            serviceCollection.TryAddSingleton<IDataFormattingService, DataFormattingService>();
            serviceCollection.TryAddSingleton<ITemplateHelperService, TemplateHelperService>();
            serviceCollection.TryAddSingleton<IStringEscapeServiceProvider, StringEscapeServiceProvider>();
            serviceCollection.TryAddSingleton<IStringEscapeService, HtmlEscapeStringService>();
            serviceCollection.TryAddSingleton<IInternalTemplateServices, InternalTemplateServices>();
            serviceCollection.TryAddSingleton<ITemplateExecutionService, TemplateExecutionService>();
            serviceCollection.TryAddSingleton<ITemplateHelperProvider, DefaultHelpers>();
            serviceCollection.TryAddSingleton<IBooleanLogicService, BooleanLogicService>();

            return serviceCollection;
        }
    }
}
