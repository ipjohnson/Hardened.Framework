using Hardened.Templates.Abstract;

namespace Hardened.Templates.Runtime.Impl;

public class DataFormattingService : IDataFormattingService {
    private readonly IDictionary<Type, FormatDataFunc> _formatters;
    private static readonly FormatDataFunc _defaultFormat = DefaultFormat;

    public DataFormattingService(IEnumerable<IDataFormatProvider> providers) {
        _formatters = new Dictionary<Type, FormatDataFunc>();

        DefaultDataFormatters.RegisterFormatters(_formatters);

        ProcessProviders(providers);
    }

    private void ProcessProviders(IEnumerable<IDataFormatProvider> providers) {
        foreach (var provider in providers) {
            provider.ProvideFormatters(_formatters);
        }
    }

    public object FormatData(ITemplateExecutionContext templateExecutionContext, string propertyName, object? data,
        string? formatString = null) {
        if (data == null) {
            return "";
        }

        if (data is SafeString safeString) {
            return safeString;
        }

        return GetFormattedStringData(templateExecutionContext, propertyName, data, formatString);
    }

    private object GetFormattedStringData(ITemplateExecutionContext templateExecutionContext, string propertyName,
        object data,
        string? formatString) {
        if (data is IFormattable) {
            return _formatters[typeof(IFormattable)](templateExecutionContext, propertyName, data, formatString);
        }

        if (_formatters.TryGetValue(data.GetType(), out var formatDataFunc)) {
            return formatDataFunc(templateExecutionContext, propertyName, data, formatString);
        }

        return data;
    }

    public FormatDataFunc LocateFormatDataFunc(Type dataType) {
        if (dataType.IsAssignableFrom(typeof(IFormattable))) {
            return _formatters[typeof(IFormattable)];
        }

        _formatters.TryGetValue(dataType, out var func);

        return func ?? _defaultFormat;
    }

    private static object DefaultFormat(ITemplateExecutionContext executionContext, string propertyName, object? data,
        string? formatString) {
        if (data == null) {
            return "";
        }

        if (data is IFormattable) {
            return DefaultDataFormatters.DefaultIFormattable(executionContext, propertyName, data, formatString);
        }

        return DefaultDataFormatters.DefaultObjectFormatter(executionContext, propertyName, data, formatString);
    }
}