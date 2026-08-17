using System.Text.Json;

namespace Hardened.Smithy.BuildTask.Parsing;

/// <summary>
/// One Smithy JSON AST, as a shape lookup.
/// </summary>
/// <remarks>
/// <para>
/// The AST is a flat map keyed by absolute shape id, and the specification guarantees that
/// "relative and forward references never need to be resolved". That is most of why this front end
/// is smaller than the OpenAPI one: there is no reference resolver, no <c>$ref</c> chasing, and no
/// distinction between a schema declared inline and one declared at the top - <c>smithy ast</c>
/// hoists and names inline structures itself, so an operation's <c>input :=</c> arrives as an
/// ordinary shape called <c>GetPetInput</c>.
/// </para>
/// <para>
/// Everything here is a read over <see cref="JsonElement"/>. Nothing is materialised into a model
/// of the AST, because the AST is walked once and the thing being built is the IR.
/// </para>
/// </remarks>
internal sealed class SmithyAst {

    private readonly Dictionary<string, JsonElement> _shapes;

    private SmithyAst(Dictionary<string, JsonElement> shapes, string version) {
        _shapes = shapes;
        Version = version;
    }

    /// <summary>The AST's own <c>smithy</c> version - <c>2.0</c> for everything this reads.</summary>
    internal string Version { get; }

    internal IReadOnlyDictionary<string, JsonElement> Shapes => _shapes;

    /// <summary>
    /// Reads an AST, or reports why it could not be read and returns null.
    /// </summary>
    /// <remarks>
    /// A zero-length document is called out separately because it is the shape of a specific
    /// failure: <c>smithy ast</c> writes to stdout and writes nothing at all when validation fails,
    /// so a redirect into a file leaves an empty file behind rather than no file. Treated as JSON it
    /// would report an unhelpful parse error at position 0.
    /// </remarks>
    internal static SmithyAst? Load(string json, ICollection<string> diagnostics) {
        if (string.IsNullOrWhiteSpace(json)) {
            diagnostics.Add(
                "the AST is empty. 'smithy ast' writes the model to stdout and writes nothing when " +
                "validation fails, so an empty file usually means the redirected command failed - " +
                "its errors went to stderr.");

            return null;
        }

        JsonDocument document;

        try {
            document = JsonDocument.Parse(json);
        } catch (JsonException exception) {
            diagnostics.Add("the AST is not valid JSON: " + exception.Message);

            return null;
        }

        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object) {
            diagnostics.Add("the AST's root is not an object.");

            return null;
        }

        var version = root.TryGetProperty("smithy", out var versionElement) &&
                      versionElement.ValueKind == JsonValueKind.String
            ? versionElement.GetString() ?? ""
            : "";

        // Reported rather than refused. A later 2.x adds shapes and traits this does not know, and
        // the unknown-trait pass says so per trait - which is more useful than declining the file.
        if (version.Length > 0 && !version.StartsWith("2.", StringComparison.Ordinal)) {
            diagnostics.Add(
                $"the AST declares Smithy version '{version}'; this reader is written against 2.0.");
        }

        if (!root.TryGetProperty("shapes", out var shapes) ||
            shapes.ValueKind != JsonValueKind.Object) {
            diagnostics.Add("the AST declares no 'shapes' object.");

            return null;
        }

        var map = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        foreach (var shape in shapes.EnumerateObject()) {
            map[shape.Name] = shape.Value;
        }

        return new SmithyAst(map, version);
    }

    internal bool TryGetShape(string shapeId, out JsonElement shape) =>
        _shapes.TryGetValue(shapeId, out shape);

    /// <summary>The shape's <c>type</c> - <c>structure</c>, <c>operation</c>, <c>list</c>.</summary>
    internal static string Kind(JsonElement shape) =>
        shape.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
            ? type.GetString() ?? ""
            : "";

    internal static bool HasTrait(JsonElement shape, string traitId) =>
        TryGetTrait(shape, traitId, out _);

    internal static bool TryGetTrait(JsonElement shape, string traitId, out JsonElement value) {
        if (shape.TryGetProperty("traits", out var traits) &&
            traits.ValueKind == JsonValueKind.Object &&
            traits.TryGetProperty(traitId, out value)) {
            return true;
        }

        value = default;

        return false;
    }

    /// <summary>Every trait applied to a shape, by id.</summary>
    internal static IEnumerable<KeyValuePair<string, JsonElement>> Traits(JsonElement shape) {
        if (!shape.TryGetProperty("traits", out var traits) ||
            traits.ValueKind != JsonValueKind.Object) {
            yield break;
        }

        foreach (var trait in traits.EnumerateObject()) {
            yield return new KeyValuePair<string, JsonElement>(trait.Name, trait.Value);
        }
    }

    /// <summary>The shape id a member, input, output or list element points at.</summary>
    internal static string? Target(JsonElement holder) =>
        holder.ValueKind == JsonValueKind.Object &&
        holder.TryGetProperty("target", out var target) &&
        target.ValueKind == JsonValueKind.String
            ? target.GetString()
            : null;

    /// <summary>A shape's members, in declaration order.</summary>
    internal static IEnumerable<KeyValuePair<string, JsonElement>> Members(JsonElement shape) {
        if (!shape.TryGetProperty("members", out var members) ||
            members.ValueKind != JsonValueKind.Object) {
            yield break;
        }

        foreach (var member in members.EnumerateObject()) {
            yield return new KeyValuePair<string, JsonElement>(member.Name, member.Value);
        }
    }

    /// <summary>The targets in a shape's array-valued property - a service's operations, an operation's errors.</summary>
    internal static IEnumerable<string> TargetList(JsonElement shape, string property) {
        if (!shape.TryGetProperty(property, out var list) ||
            list.ValueKind != JsonValueKind.Array) {
            yield break;
        }

        foreach (var entry in list.EnumerateArray()) {
            var target = Target(entry);

            if (target != null) {
                yield return target;
            }
        }
    }

    /// <summary>
    /// Whether the shape is a trait definition rather than something to generate.
    /// </summary>
    /// <remarks>
    /// This is not a nicety. A model that declares maven dependencies gets their trait definitions
    /// as shapes in its own AST - declaring <c>smithy-aws-traits</c> alone takes an eight-shape
    /// model to forty-three, twenty-six of them trait definitions - and every one of those would
    /// otherwise become a record named after an AWS trait.
    /// </remarks>
    internal static bool IsTraitDefinition(JsonElement shape) =>
        HasTrait(shape, SmithyTraits.Trait);

    /// <summary>Whether the shape is one the model author wrote, rather than one a dependency brought.</summary>
    internal static bool IsInNamespace(string shapeId, string ns) =>
        shapeId.StartsWith(ns + "#", StringComparison.Ordinal);

    /// <summary>The namespace part of a shape id.</summary>
    internal static string NamespaceOf(string shapeId) {
        var hash = shapeId.IndexOf('#');

        return hash > 0 ? shapeId.Substring(0, hash) : "";
    }
}
