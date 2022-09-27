using Hardened.Requests.Abstract.Execution;

namespace Hardened.Templates.Abstract
{
    public interface ITemplateExecutionContext
    {
        ITemplateOutputWriter Writer { get; }

        ITemplateExecutionService ExecutionService { get; }

        IInternalTemplateServices TemplateServices { get; }

        IStringEscapeService StringEscapeService { get; }

        string TemplateExtension { get; }

        IServiceProvider RequestServiceProvider { get; }

        ITemplateExecutionContext? ParentContext { get; }

        IExecutionContext? ExecutionContext { get; }

        object ObjectValue { get; }

        object? GetCustomValue(string key);

        void SetCustomValue(string key, object value);

        SafeString GetEscapedString(object value, string propertyName = "", string formattingString = "");
    }
}
