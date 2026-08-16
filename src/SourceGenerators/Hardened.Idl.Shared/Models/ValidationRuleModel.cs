namespace Hardened.Idl.Models;

internal class ValidationRuleModel {
    public string ParameterName { get; set; } = "";
    public bool IsRequired { get; set; }
    public int? MinLength { get; set; }
    public int? MaxLength { get; set; }
    public decimal? Minimum { get; set; }
    public decimal? Maximum { get; set; }
    public bool ExclusiveMinimum { get; set; }
    public bool ExclusiveMaximum { get; set; }
    public string? Pattern { get; set; }
    public int? MinItems { get; set; }
    public int? MaxItems { get; set; }
    public List<string>? EnumValues { get; set; }

    public bool HasAnyRules =>
        IsRequired || MinLength.HasValue || MaxLength.HasValue ||
        Minimum.HasValue || Maximum.HasValue ||
        ExclusiveMinimum || ExclusiveMaximum ||
        Pattern != null || MinItems.HasValue || MaxItems.HasValue ||
        EnumValues is { Count: > 0 };

    public static ValidationRuleModel FromParameter(ParameterModel param) {
        return new ValidationRuleModel {
            ParameterName = param.MemberName,
            IsRequired = param.IsRequired,
            MinLength = param.MinLength,
            MaxLength = param.MaxLength,
            Minimum = param.Minimum,
            Maximum = param.Maximum,
            ExclusiveMinimum = param.ExclusiveMinimum,
            ExclusiveMaximum = param.ExclusiveMaximum,
            Pattern = param.Pattern,
            MinItems = param.MinItems,
            MaxItems = param.MaxItems,
            EnumValues = param.EnumValues
        };
    }

    public static ValidationRuleModel FromProperty(PropertyModel prop) {
        return new ValidationRuleModel {
            ParameterName = prop.Name,
            IsRequired = prop.IsRequired,
            MinLength = prop.MinLength,
            MaxLength = prop.MaxLength,
            Minimum = prop.Minimum,
            Maximum = prop.Maximum,
            ExclusiveMinimum = prop.ExclusiveMinimum,
            ExclusiveMaximum = prop.ExclusiveMaximum,
            Pattern = prop.Pattern,
            MinItems = prop.MinItems,
            MaxItems = prop.MaxItems,
            EnumValues = prop.EnumValues
        };
    }
}
