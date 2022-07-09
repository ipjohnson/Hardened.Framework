using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Shared.Runtime.Application;
using Hardened.Templates.Abstract;
using Hardened.Templates.Runtime.Impl;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hardened.Templates.Runtime.DependencyInjection
{
    public static class TemplateDI
    {
        public static void Register(IEnvironment environment, IServiceCollection serviceCollection)
        {
            serviceCollection.TryAddSingleton<IBooleanLogicService, BooleanLogicService>();
            serviceCollection.TryAddSingleton<IDataFormattingService, DataFormattingService>();
            serviceCollection.TryAddSingleton<ITemplateExecutionService, TemplateExecutionService>();
            serviceCollection.TryAddSingleton<ITemplateHelperService, TemplateHelperService>();
            serviceCollection.TryAddSingleton<IStringEscapeServiceProvider, StringEscapeServiceProvider>();
            serviceCollection.TryAddSingleton<IStringEscapeService, HtmlEscapeStringService>();
            serviceCollection.TryAddSingleton<IInternalTemplateServices, InternalTemplateServices>();
        }
    }
}
