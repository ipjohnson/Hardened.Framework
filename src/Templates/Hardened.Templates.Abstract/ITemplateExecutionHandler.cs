using Hardened.Requests.Abstract.Execution;

namespace Hardened.Templates.Abstract;

public interface ITemplateExecutionHandler
{
    Task Execute(
        object requestValue,
        IServiceProvider serviceProvider,
        ITemplateOutputWriter writer,
        ITemplateExecutionContext? parentContext, 
        IExecutionContext? executionContext);
}