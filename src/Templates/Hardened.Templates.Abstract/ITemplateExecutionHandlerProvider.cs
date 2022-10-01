namespace Hardened.Templates.Abstract;

public interface ITemplateExecutionHandlerProvider
{
    ITemplateExecutionService? TemplateExecutionService { get; set; }

    ITemplateExecutionHandler? GetTemplateExecutionHandler(string templateName);
}