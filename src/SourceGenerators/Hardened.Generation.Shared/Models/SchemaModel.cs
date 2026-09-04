namespace Hardened.Generation.Models;

internal class SchemaModel : IEquatable<SchemaModel> {
    public string Name { get; set; } = "";
    public SchemaKind Kind { get; set; }

    /// <summary>The spec's <c>deprecated</c>, which becomes <c>[Obsolete]</c>.</summary>
    public bool IsDeprecated { get; set; }

    /// <summary>
    /// Whether the description declared this schema as an error rather than as a payload.
    /// </summary>
    /// <remarks>
    /// Set by the Smithy front end for a shape carrying <c>@error</c>, and false for everything
    /// OpenAPI produces, which has no way to say it: a <c>$ref</c> under a 4xx response is a
    /// schema like any other. Provenance rather than shape, because the thing it licenses -
    /// filling a required <c>message</c> in <c>DefaultErrorBody</c> - is only safe where the
    /// description says the member is an error message, and <c>{ message: string }</c> is far too
    /// ordinary a shape to read that off.
    /// </remarks>
    public bool IsErrorShape { get; set; }

    /// <summary>The schema's <c>description</c>, as the generated type's doc comment.</summary>
    public string? Description { get; set; }
    public List<PropertyModel> Properties { get; set; } = new();

    /// <summary>
    /// The schemas a <see cref="SchemaKind.OneOf"/> may hold, in declaration order.
    /// </summary>
    /// <remarks>
    /// The document says a payload is exactly one of these, so the generated type says the same: it
    /// carries the value as <c>object</c> and refuses anything that is not one of them. Order is the
    /// document's, because it decides the order branches are tried when there is no discriminator to
    /// key on.
    /// </remarks>
    public List<ChoiceBranchModel> OneOf { get; set; } = new();
    public List<string> EnumValues { get; set; } = new();

    /// <summary>
    /// The C# member name for each entry of <see cref="EnumValues"/>, in the same order.
    /// </summary>
    /// <remarks>
    /// Allocated rather than derived, because two wire values can reach C# as one member name -
    /// Elasticsearch's column enums carry <c>buckets.count</c> beside <c>bc</c> and a dozen more
    /// that PascalCase alike, and Docker and Cloudflare both declare the empty string as a value.
    /// </remarks>
    public List<string> EnumMemberNames { get; set; } = new();

    /// <summary>
    /// Whether <see cref="EnumMemberNames"/> is what the document asked for, or a parser's guess.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both front ends fill that list, and they mean different things by it. OpenAPI fills it from
    /// <c>x-enum-varnames</c>, which is an author naming the C# members outright. Smithy fills it
    /// from the member key beside the wire value - <c>DOG = "dog"</c> - which is a hint rather than
    /// a request, and one that PascalCases to <c>DOG</c> where the wire value gives the more
    /// idiomatic <c>Dog</c>.
    /// </para>
    /// <para>
    /// So the allocator honours the list when this is set and prefers the wire value otherwise,
    /// which is what it has always done for Smithy. Without the distinction, turning on
    /// <c>x-enum-varnames</c> would have renamed every Smithy-generated enum member as a side
    /// effect.
    /// </para>
    /// </remarks>
    public bool EnumMemberNamesAreDeclared { get; set; }

    /// <summary>The member names to emit, derived when the allocator has not run.</summary>
    public IReadOnlyList<string> EnumMembers =>
        EnumMemberNames.Count == EnumValues.Count
            ? EnumMemberNames
            : EnumValues.ConvertAll(Generation.NamingHelper.ToPascalCase);
    public List<string> Required { get; set; } = new();
    public string? ArrayItemsRef { get; set; }
    public string? ArrayItemsType { get; set; }
    public string? ArrayItemsFormat { get; set; }

    /// <summary>
    /// The item-count bounds a named array schema declares on itself. A property's bounds live on
    /// <see cref="PropertyModel"/>; these are for the schema a contract names at the top level,
    /// whose bound previously had nowhere to land.
    /// </summary>
    public int? MinItems { get; set; }
    public int? MaxItems { get; set; }
    public string? DictionaryValueType { get; set; }
    public string? DictionaryValueRef { get; set; }
    public string? DictionaryValueFormat { get; set; }
    public string? Type { get; set; }
    public string? Format { get; set; }

    /// <summary>
    /// The property carrying the type discriminator, from <c>discriminator.propertyName</c>. Set on
    /// the base of a polymorphic hierarchy and null everywhere else.
    /// </summary>
    public string? DiscriminatorPropertyName { get; set; }

    /// <summary>
    /// Discriminator value to schema reference, in document order.
    /// </summary>
    /// <remarks>
    /// A list rather than a dictionary because the serialized form has to be byte-stable - the
    /// build task compares content to decide whether to rewrite the model file, and a reordered
    /// mapping would make every build look dirty.
    /// </remarks>
    public List<DiscriminatorMappingModel> DiscriminatorMapping { get; set; } = new();

    /// <summary>
    /// The schema this one derives from, as a reference. Set from an <c>allOf</c> branch pointing at
    /// a schema that carries a discriminator.
    /// </summary>
    public string? BaseRef { get; set; }

    /// <summary>
    /// Whether this schema is the base of a hierarchy, which is what makes it abstract and gives it
    /// polymorphism metadata in the generated resolver.
    /// </summary>
    public bool IsPolymorphicBase => DiscriminatorPropertyName != null;

    public bool Equals(SchemaModel? other) {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Name == other.Name && Kind == other.Kind &&
               Description == other.Description && IsDeprecated == other.IsDeprecated &&
               IsErrorShape == other.IsErrorShape &&
               Type == other.Type && Format == other.Format &&
               ArrayItemsRef == other.ArrayItemsRef &&
               ArrayItemsType == other.ArrayItemsType &&
               ArrayItemsFormat == other.ArrayItemsFormat &&
               MinItems == other.MinItems &&
               MaxItems == other.MaxItems &&
               DictionaryValueType == other.DictionaryValueType &&
               DictionaryValueRef == other.DictionaryValueRef &&
               DictionaryValueFormat == other.DictionaryValueFormat &&
               DiscriminatorPropertyName == other.DiscriminatorPropertyName &&
               BaseRef == other.BaseRef &&
               DiscriminatorMapping.SequenceEqual(other.DiscriminatorMapping) &&
               Properties.SequenceEqual(other.Properties) &&
               EnumValues.SequenceEqual(other.EnumValues) &&
               Required.SequenceEqual(other.Required);
    }

    public override bool Equals(object? obj) => Equals(obj as SchemaModel);

    public override int GetHashCode() {
        unchecked {
            var hash = Name.GetHashCode();
            hash = (hash * 397) ^ Kind.GetHashCode();
            return hash;
        }
    }
}

internal enum SchemaKind {
    Object,
    Enum,
    Array,
    Primitive,
    Dictionary,

    /// <summary>
    /// A choice between named schemas - a <c>oneOf</c>, which becomes a type holding exactly one of
    /// them. See <see cref="SchemaModel.OneOf"/>.
    /// </summary>
    OneOf
}
