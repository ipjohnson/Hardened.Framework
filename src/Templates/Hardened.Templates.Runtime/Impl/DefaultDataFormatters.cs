using Hardened.Templates.Abstract;

namespace Hardened.Templates.Runtime.Impl;

public static class DefaultDataFormatters
{
    public static void RegisterFormatters(IDictionary<Type, FormatDataFunc> formatters)
    {
        formatters[typeof(IFormattable)] = DefaultIFormattable;
    }

    public static object DefaultIFormattable(ITemplateExecutionContext executionContext, string propertyName, object? data, string? formatString)
    {
        if (data == null)
        {
            return "";
        }

        return ((IFormattable)data).ToString(formatString, null);
    }

    public static object DefaultObjectFormatter(ITemplateExecutionContext executionContext, string propertyName,
        object? data, string? formatString)
    {
        if (data == null)
        {
            return "";
        }

        return data.ToString()!;
    }
}