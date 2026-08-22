using System.Collections.Generic;
using System.Linq;

namespace Hardened.SourceGenerator.Models.Request;

/// <summary>One entry of <c>components/schemas</c>, as written JSON.</summary>
/// <remarks>
/// Carried as text rather than as a model because nothing between here and the emitted document
/// needs to look inside it, and a string compares by value - which is what keeps the handler model
/// usable as a Roslyn incremental cache key.
/// </remarks>
public sealed class SchemaComponent : System.IEquatable<SchemaComponent> {
    public SchemaComponent(string name, string json) {
        Name = name;
        Json = json;
    }

    public string Name { get; }

    public string Json { get; }

    public bool Equals(SchemaComponent? other) =>
        other is not null && Name == other.Name && Json == other.Json;

    public override bool Equals(object? obj) => Equals(obj as SchemaComponent);

    public override int GetHashCode() {
        unchecked {
            return (Name.GetHashCode() * 397) ^ Json.GetHashCode();
        }
    }
}

/// <summary>One enum member, and the value it goes on the wire as.</summary>
public sealed class EnumWireValue : System.IEquatable<EnumWireValue> {
    public EnumWireValue(string member, string wire) {
        Member = member;
        Wire = wire;
    }

    /// <summary>The C# member name.</summary>
    public string Member { get; }

    /// <summary>What a client sends and receives.</summary>
    public string Wire { get; }

    public bool Equals(EnumWireValue? other) =>
        other is not null && Member == other.Member && Wire == other.Wire;

    public override bool Equals(object? obj) => Equals(obj as EnumWireValue);

    public override int GetHashCode() {
        unchecked {
            return (Member.GetHashCode() * 397) ^ Wire.GetHashCode();
        }
    }
}

/// <summary>
/// An enum a handler puts on the wire, with the vocabulary it was resolved to.
/// </summary>
/// <remarks>
/// Collected while the schema is written, because that is the only point at which a Roslyn symbol
/// still exists and the members can be read at all - the same reason the schema itself is converted
/// to text there. The routing table generator turns these into converters, so the bytes and the
/// document are produced from one resolution rather than two.
/// </remarks>
public sealed class EnumVocabulary : System.IEquatable<EnumVocabulary> {
    public EnumVocabulary(
        string qualifiedName, string name, string naming, IReadOnlyList<EnumWireValue> values) {
        QualifiedName = qualifiedName;
        Name = name;
        Naming = naming;
        Values = values;
    }

    /// <summary>Fully qualified and <c>global::</c>-prefixed, as emitted code names it.</summary>
    public string QualifiedName { get; }

    /// <summary>The bare type name, which names the converter and appears in binding errors.</summary>
    public string Name { get; }

    /// <summary>The resolved <c>EnumNaming</c> member name.</summary>
    public string Naming { get; }

    public IReadOnlyList<EnumWireValue> Values { get; }

    public bool Equals(EnumVocabulary? other) =>
        other is not null &&
        QualifiedName == other.QualifiedName &&
        Naming == other.Naming &&
        Values.SequenceEqual(other.Values);

    public override bool Equals(object? obj) => Equals(obj as EnumVocabulary);

    public override int GetHashCode() {
        unchecked {
            return (QualifiedName.GetHashCode() * 397) ^ Naming.GetHashCode();
        }
    }
}

/// <summary>
/// A type's JSON Schema, and every named schema it reaches.
/// </summary>
public sealed class HandlerSchema : System.IEquatable<HandlerSchema> {
    public HandlerSchema(
        string schema,
        IReadOnlyList<SchemaComponent> components,
        IReadOnlyList<EnumVocabulary>? enums = null) {
        Schema = schema;
        Components = components;
        Enums = enums ?? System.Array.Empty<EnumVocabulary>();
    }

    /// <summary>The schema itself, usually a <c>$ref</c> into <see cref="Components"/>.</summary>
    public string Schema { get; }

    public IReadOnlyList<SchemaComponent> Components { get; }

    /// <summary>Every enum this type reaches, with its wire vocabulary.</summary>
    public IReadOnlyList<EnumVocabulary> Enums { get; }

    public bool Equals(HandlerSchema? other) =>
        other is not null &&
        Schema == other.Schema &&
        Components.SequenceEqual(other.Components) &&
        Enums.SequenceEqual(other.Enums);

    public override bool Equals(object? obj) => Equals(obj as HandlerSchema);

    public override int GetHashCode() => Schema.GetHashCode();
}
