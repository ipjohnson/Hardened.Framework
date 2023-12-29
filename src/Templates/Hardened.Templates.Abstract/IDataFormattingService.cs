namespace Hardened.Templates.Abstract;

public delegate object FormatDataFunc(ITemplateExecutionContext templateExecutionContext, string propertyName,
    object? data, string? formatString = null);

public interface IDataFormattingService {
    object FormatData(ITemplateExecutionContext templateExecutionContext, string propertyName, object? data,
        string? formatString = null);

    FormatDataFunc? LocateFormatDataFunc(Type dataType);
}