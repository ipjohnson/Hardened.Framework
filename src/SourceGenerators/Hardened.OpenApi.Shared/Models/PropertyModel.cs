namespace Hardened.OpenApi.SourceGenerator.Models;

internal class PropertyModel : IEquatable<PropertyModel>, IConstraintFacets {
    public string Name { get; set; } = "";

    /// <summary>The property's <c>description</c>, as its <c>&lt;param&gt;</c> doc comment.</summary>
    public string? Description { get; set; }
    public string? Type { get; set; }
    public string? Format { get; set; }
    public string? Ref { get; set; }
    public bool IsArray { get; set; }
    public string? ArrayItemsRef { get; set; }
    public string? ArrayItemsType { get; set; }
    public string? ArrayItemsFormat { get; set; }
    public bool IsRequired { get; set; }

    /// <summary>
    /// The schema's <c>nullable</c>. Orthogonal to <see cref="IsRequired"/> in OpenAPI 3.0: a value
    /// may be required to be present and still permitted to be null.
    /// </summary>
    public bool IsNullable { get; set; }

    /// <summary>
    /// Whether the generated C# type is nullable — required-and-nullable is <c>string?</c> too.
    /// </summary>
    public bool IsCSharpNullable => !IsRequired || IsNullable;

    /// <summary>
    /// Whether the generated parameter carries <c>= default</c>. Requiredness alone decides this:
    /// a required-but-nullable value still has to be supplied.
    /// </summary>
    public bool HasDefault => !IsRequired;

    /// <summary>
    /// Whether a <c>[Required]</c> constraint applies. It does not when the spec permits null —
    /// ValidationModules' <c>[Required]</c> rejects null, which would refuse a value the spec
    /// allows.
    /// </summary>
    public bool ConstrainedAsRequired => IsRequired && !IsNullable;

    /// <summary>
    /// The spec's <c>default</c>, as written. Rendered into a C# literal at emit time, because what
    /// it renders as depends on the type the value lands in.
    /// </summary>
    public string? Default { get; set; }
    public bool IsDictionary { get; set; }
    public string? DictionaryValueType { get; set; }
    public string? DictionaryValueRef { get; set; }
    public List<string>? EnumValues { get; set; }

    // Validation constraints
    public int? MinLength { get; set; }
    public int? MaxLength { get; set; }
    public decimal? Minimum { get; set; }
    public decimal? Maximum { get; set; }
    public bool ExclusiveMinimum { get; set; }
    public bool ExclusiveMaximum { get; set; }
    public string? Pattern { get; set; }
    public int? MinItems { get; set; }
    public int? MaxItems { get; set; }

    public bool HasValidationConstraints =>
        IsRequired || MinLength.HasValue || MaxLength.HasValue ||
        Minimum.HasValue || Maximum.HasValue ||
        ExclusiveMinimum || ExclusiveMaximum ||
        Pattern != null || MinItems.HasValue || MaxItems.HasValue ||
        EnumValues is { Count: > 0 };

    public bool Equals(PropertyModel? other) {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Name == other.Name && Type == other.Type && Format == other.Format &&
               Description == other.Description &&
               Ref == other.Ref && IsArray == other.IsArray && IsRequired == other.IsRequired && IsNullable == other.IsNullable && Default == other.Default &&
               MinLength == other.MinLength && MaxLength == other.MaxLength &&
               Minimum == other.Minimum && Maximum == other.Maximum &&
               ExclusiveMinimum == other.ExclusiveMinimum && ExclusiveMaximum == other.ExclusiveMaximum &&
               Pattern == other.Pattern && MinItems == other.MinItems && MaxItems == other.MaxItems;
    }

    public override bool Equals(object? obj) => Equals(obj as PropertyModel);

    public override int GetHashCode() {
        unchecked {
            var hash = Name.GetHashCode();
            hash = (hash * 397) ^ (Type?.GetHashCode() ?? 0);
            hash = (hash * 397) ^ IsRequired.GetHashCode();
            hash = (hash * 397) ^ (MinLength?.GetHashCode() ?? 0);
            hash = (hash * 397) ^ (MaxLength?.GetHashCode() ?? 0);
            hash = (hash * 397) ^ (Pattern?.GetHashCode() ?? 0);
            return hash;
        }
    }
}
