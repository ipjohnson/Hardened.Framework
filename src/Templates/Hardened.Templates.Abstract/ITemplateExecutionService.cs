using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Hardened.Requests.Abstract.Execution;

namespace Hardened.Templates.Abstract
{

    public delegate Task TemplateExecutionFunction(
        object templateData, 
        IServiceProvider serviceProvider, 
        ITemplateOutputWriter writer,
        ITemplateExecutionContext? parentContext,
        IExecutionContext? executionContext);

    public interface ITemplateExecutionService
    {
        Task<string> Execute(string templateName, object templateData, IServiceProvider serviceProvider);

        Task Execute(
            string templateName,
            object? templateData, 
            IServiceProvider serviceProvider, 
            ITemplateOutputWriter writer,
            ITemplateExecutionContext? parentContext, 
            IExecutionContext? executionContext);
        
        TemplateExecutionFunction? FindTemplateExecutionFunction(string templateName);
    }
}
