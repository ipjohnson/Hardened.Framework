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

/// <summary>
/// A type's JSON Schema, and every named schema it reaches.
/// </summary>
public sealed class HandlerSchema : System.IEquatable<HandlerSchema> {
    public HandlerSchema(string schema, IReadOnlyList<SchemaComponent> components) {
        Schema = schema;
        Components = components;
    }

    /// <summary>The schema itself, usually a <c>$ref</c> into <see cref="Components"/>.</summary>
    public string Schema { get; }

    public IReadOnlyList<SchemaComponent> Components { get; }

    public bool Equals(HandlerSchema? other) =>
        other is not null && Schema == other.Schema && Components.SequenceEqual(other.Components);

    public override bool Equals(object? obj) => Equals(obj as HandlerSchema);

    public override int GetHashCode() => Schema.GetHashCode();
}
