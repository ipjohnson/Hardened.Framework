namespace Hardened.Idl.Models;

internal class ParameterModel : IEquatable<ParameterModel>, IConstraintFacets {
    public string Name { get; set; } = "";
    public string In { get; set; } = "";
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

    /// <summary>The parameter's <c>description</c>, as its <c>&lt;param&gt;</c> doc comment.</summary>
    public string? Description { get; set; }
    public string? Type { get; set; }
    public string? Format { get; set; }
    /// <summary>
    /// The C# member name, where it cannot be the camelCased wire name.
    /// </summary>
    /// <remarks>
    /// OpenAPI scopes a parameter's uniqueness to its name <em>and</em> location, so one operation
    /// may legally declare two called the same thing: Kubernetes' proxy routes take <c>path</c> in
    /// the path and <c>path</c> in the query. Both would generate one member.
    /// </remarks>
    public string? MemberNameOverride { get; set; }

    /// <summary>The name this parameter carries in generated C#.</summary>
    public string MemberName => MemberNameOverride ?? Idl.NamingHelper.ToParameterName(Name);

    public string? Ref { get; set; }
    public bool IsArray { get; set; }
    public string? ArrayItemsType { get; set; }
    public string? ArrayItemsRef { get; set; }

    // Validation constraints
    public int? MinLength { get; set; }
    public int? MaxLength { get; set; }
    public decimal? Minimum { get; set; }
    public decimal? Maximum { get; set; }
    public bool ExclusiveMinimum { get; set; }
    public bool ExclusiveMaximum { get; set; }
    public string? Pattern { get; set; }

    /// <summary>
    /// The route-constraint name this parameter contributes to its route template, or null.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Set only for <c>path</c> parameters. A constraint on a path segment narrows which URLs name
    /// a resource, so a violation means the route did not match and the answer is 404 - the same
    /// reasoning that makes <c>/pets/</c> a 404 against <c>/pets/{petId}</c> rather than a 400 from
    /// the binder. A constraint on a query, header or body parameter is a judgement about a request
    /// that did name a resource, stays on the validation path, and answers 400.
    /// </para>
    /// <para>
    /// Additive to the serialized model rather than a version bump: an older file yields null,
    /// which is the behaviour before this existed.
    /// </para>
    /// </remarks>
    public string? RouteConstraint { get; set; }
    public int? MinItems { get; set; }
    public int? MaxItems { get; set; }
    public List<string>? EnumValues { get; set; }

    public bool HasValidationConstraints =>
        IsRequired || MinLength.HasValue || MaxLength.HasValue ||
        Minimum.HasValue || Maximum.HasValue ||
        ExclusiveMinimum || ExclusiveMaximum ||
        Pattern != null || MinItems.HasValue || MaxItems.HasValue ||
        EnumValues is { Count: > 0 };

    public bool Equals(ParameterModel? other) {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Name == other.Name && In == other.In && IsRequired == other.IsRequired && IsNullable == other.IsNullable && Default == other.Default &&
               Description == other.Description &&
               Type == other.Type && Format == other.Format &&
               MinLength == other.MinLength && MaxLength == other.MaxLength &&
               Minimum == other.Minimum && Maximum == other.Maximum &&
               ExclusiveMinimum == other.ExclusiveMinimum && ExclusiveMaximum == other.ExclusiveMaximum &&
               Pattern == other.Pattern && MinItems == other.MinItems && MaxItems == other.MaxItems;
    }

    public override bool Equals(object? obj) => Equals(obj as ParameterModel);

    public override int GetHashCode() {
        unchecked {
            var hash = Name.GetHashCode();
            hash = (hash * 397) ^ In.GetHashCode();
            hash = (hash * 397) ^ IsRequired.GetHashCode();
            hash = (hash * 397) ^ (MinLength?.GetHashCode() ?? 0);
            hash = (hash * 397) ^ (MaxLength?.GetHashCode() ?? 0);
            hash = (hash * 397) ^ (Pattern?.GetHashCode() ?? 0);
            return hash;
        }
    }
}
