namespace Hardened.Templates.Abstract
{
    public interface ITemplateHelper
    {
        ValueTask<object> Execute(ITemplateExecutionContext handlerDataContext, params object[] arguments);
    }
}
