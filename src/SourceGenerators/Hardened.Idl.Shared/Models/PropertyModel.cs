namespace Hardened.Idl.Models;

internal class PropertyModel : IEquatable<PropertyModel>, IConstraintFacets {
    public string Name { get; set; } = "";

    /// <summary>
    /// The C# member name, where it cannot be the PascalCased wire name.
    /// </summary>
    /// <remarks>
    /// A schema declaring a property of its own name - GitHub's <c>commit.commit</c>, Stripe's
    /// <c>error.error</c> - would emit a member named after its enclosing type, which C# forbids
    /// (CS0542). That used to fail the build with advice to rename one of them, which is not advice
    /// a consumer of someone else's published document can take. The wire name is pinned by
    /// <c>[JsonPropertyName]</c> either way, so renaming the member costs nothing over the wire.
    /// </remarks>
    public string? MemberNameOverride { get; set; }

    /// <summary>The name this property carries in generated C#.</summary>
    public string MemberName => MemberNameOverride ?? Idl.NamingHelper.ToPascalCase(Name);

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
    /// The schema's <c>readOnly</c>: the property appears in responses and must not be sent in a
    /// request.
    /// </summary>
    public bool IsReadOnly { get; set; }

    /// <summary>
    /// The schema's <c>writeOnly</c>: the property is accepted in requests and must not appear in a
    /// response.
    /// </summary>
    public bool IsWriteOnly { get; set; }

    /// <summary>
    /// Whether the generated record declares this positionally.
    /// </summary>
    /// <remarks>
    /// A <c>readOnly</c> property is declared as an init-only member instead, which is what keeps it
    /// out of deserialization: it is not a constructor parameter, and the resolver gives it no
    /// setter, so a client sending it has the value discarded rather than honoured.
    /// </remarks>
    public bool IsConstructorParameter => !IsReadOnly;

    /// <summary>
    /// Whether validation constraints are emitted for this property at all.
    /// </summary>
    /// <remarks>
    /// Validation runs on request binding, and a <c>readOnly</c> property is never client-supplied —
    /// it arrives as its type default no matter what the client sent. So every constraint on it is
    /// either dead or actively wrong: <c>required</c> + <c>readOnly</c> would reject the create
    /// request of a client that correctly omitted the value. Requiredness in OpenAPI is scoped to a
    /// direction, and this one says "always present in a response".
    /// </remarks>
    public bool Constrained => !IsReadOnly;

    /// <summary>
    /// The spec's <c>default</c>, as written. Rendered into a C# literal at emit time, because what
    /// it renders as depends on the type the value lands in.
    /// </summary>
    public string? Default { get; set; }
    public bool IsDictionary { get; set; }
    public string? DictionaryValueType { get; set; }
    public string? DictionaryValueRef { get; set; }
    public List<string>? EnumValues { get; set; }

    /// <summary>
    /// The schemas a <c>oneOf</c> or <c>anyOf</c> property may hold.
    /// </summary>
    /// <remarks>
    /// The property itself is typed <c>JsonElement</c> for now, so nothing in the emitted code
    /// names these - and that is exactly why they are recorded. They are what the payload is
    /// allowed to be, so they are part of the contract and have to be generated for a caller to
    /// deserialize into or switch over. Without this the reachability pass sees a property nothing
    /// points from, and the branch types are not emitted at all.
    /// </remarks>
    public List<string> OneOfRefs { get; set; } = new();

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
               IsReadOnly == other.IsReadOnly && IsWriteOnly == other.IsWriteOnly &&
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
