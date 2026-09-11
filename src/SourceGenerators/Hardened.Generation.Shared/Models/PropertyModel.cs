namespace Hardened.Generation.Models;

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
    public string MemberName => MemberNameOverride ?? Generation.NamingHelper.ToPascalCase(Name);

    /// <summary>
    /// The response header this member is bound to, or null where it is part of the body.
    /// </summary>
    /// <remarks>
    /// Smithy's <c>@httpHeader</c> on an output member. The member stays on the record - it is one
    /// of the structure's members and the handler sets it - and stops being serialized, because it
    /// leaves as a header instead. Reading the trait and leaving the member in the body sent the
    /// value as a JSON property with the header's name lower-cased, which is the state this
    /// replaces.
    /// </remarks>
    public string? HeaderName { get; set; }

    /// <summary>Whether this member leaves as a header rather than in the body.</summary>
    public bool IsHeaderBound => HeaderName != null;

    /// <summary>
    /// The integer this member is keyed by in MessagePack, from <c>x-message-pack-index</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Null where the contract states none, which is a build error under the keyed serializer and
    /// means nothing under the other two - see <c>SpecDiagnostics</c>. Nothing invents one. A keyed
    /// wire format is worth having because the key is stable, and an index this build chose would
    /// move the moment a property was inserted above it, breaking every client generated before the
    /// move with a document diff that reads as an addition.
    /// </para>
    /// <para>
    /// Written back into the published document by <c>SpecSchemaWriter</c>, so a client generated
    /// from the document agrees with the server about field identity rather than about property
    /// names.
    /// </para>
    /// </remarks>
    public int? MessagePackIndex { get; set; }

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
    /// <remarks>
    /// A <c>readOnly</c> property is nullable whatever the description says about it, because
    /// <c>required</c> there means "always present in a response" and the same type is what a client
    /// sends in a request. Non-nullable, it became a positional parameter with no default and a
    /// create call that correctly omitted the server-owned id was answered 400.
    /// </remarks>
    public bool IsCSharpNullable => !IsRequired || IsNullable || IsReadOnly;

    /// <summary>
    /// Whether the generated parameter carries <c>= default</c>. Requiredness alone decides this:
    /// a required-but-nullable value still has to be supplied.
    /// </summary>
    public bool HasDefault => !IsRequired || IsReadOnly;

    /// <summary>
    /// Whether a <c>[Required]</c> constraint applies. It does not when the spec permits null —
    /// ValidationModules' <c>[Required]</c> rejects null, which would refuse a value the spec
    /// allows.
    /// </summary>
    /// <remarks>
    /// Never for a <c>readOnly</c> property. <c>required</c> on one means "always present in a
    /// response", and validation runs on request binding - so demanding it rejects the create call
    /// of a client that correctly omitted a value the server assigns.
    /// </remarks>
    public bool ConstrainedAsRequired => IsRequired && !IsNullable && !IsReadOnly;

    /// <summary>
    /// Whether a response leaves the member out rather than writing <c>null</c> into it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Optional and not nullable means "may be absent, and is of this type if present" - so
    /// <c>null</c> is a value the document forbids. Nothing said so, and every generated model
    /// wrote one for every optional member the handler had nothing to put in: a client whose
    /// generated <c>expiresAt</c> is <c>long</c> - which is what the contract describes, and what a
    /// Refitter interface and a Swift <c>Codable</c> struct both produce - refuses the body with
    /// "Cannot get the value of a token type 'Null' as a number". Kiota makes every property
    /// nullable and read the invalid payload happily, which is why it survived.
    /// </para>
    /// <para>
    /// Required-and-nullable is the opposite case and is left alone: the document says the member is
    /// always present and may be null, so writing the null is the contract being kept. Optional-and-
    /// nullable permits both, and keeps what it did.
    /// </para>
    /// <para>
    /// A header-bound member is already <c>[JsonIgnore]</c> in both directions, and a second ignore
    /// attribute on one declaration does not compile.
    /// </para>
    /// </remarks>
    public bool OmittedWhenNull => !IsRequired && !IsNullable && !IsHeaderBound;

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
    /// Every property, including the <c>readOnly</c> ones. They used to be init-only members the
    /// resolver gave no setter, which kept a client from sending them - and also kept anything from
    /// reading them, so a response's <c>created_at</c> and <c>id</c> were dropped on the way in.
    /// Direction is a fact about the contract rather than about the type, so it is documented with
    /// <c>[ResponseOnly]</c> and enforced where it can name the property, not by withholding an
    /// accessor.
    /// </remarks>
    public bool IsConstructorParameter => true;

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
    public string? DictionaryValueFormat { get; set; }
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
    public List<ChoiceBranchModel> OneOf { get; set; } = new();

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
               Pattern == other.Pattern && MinItems == other.MinItems && MaxItems == other.MaxItems &&
               HeaderName == other.HeaderName && MessagePackIndex == other.MessagePackIndex;
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
