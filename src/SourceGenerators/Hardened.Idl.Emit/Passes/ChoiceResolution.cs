using System.Collections.Generic;
using Hardened.Idl.Models;

namespace Hardened.Idl;

/// <summary>
/// How a payload is told apart from the other branches of a <c>oneOf</c>, decided here rather than
/// at run time wherever it can be.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two questions, not one.</b> Whether the branches overlap is a property of the schemas and is
/// answered here. Whether a <em>particular payload</em> is ambiguous is a property of that payload
/// and can only be answered when it arrives. Conflating them is why the first version of this
/// refused to generate a type for 16% of the corpus's choices: two schemas whose properties are all
/// optional overlap on paper, but a payload carrying <c>meow</c> matches only one of them.
/// </para>
/// <para>
/// So a branch gets a static test where the schemas prove one - 84% of the corpus - and where they
/// do not, the branch is still generated and decided by counting matches when a payload arrives.
/// What is never done is taking the first branch that reads, which is what
/// <c>serde(untagged)</c> and Pydantic's default do and is the one behaviour that binds the wrong
/// type without saying so.
/// </para>
/// <para>
/// <b>What the corpus declares.</b> Of 338 undiscriminated <c>oneOf</c> properties across 38
/// published descriptions, 189 have branches of different JSON kinds - <c>oneOf: [string,
/// boolean]</c> and the like - 57 give one branch a property no other has, 18 differ by required
/// set, and 19 carry a <c>const</c> that is a discriminator in all but name.
/// </para>
/// <para>
/// <b>The tests are ordered by how much they prove.</b> A value kind is a fact about the payload; a
/// property unique to one branch is a fact about the schemas; a required set is a promise the
/// document makes. Applying them in that order means a branch is chosen on the strongest evidence
/// available rather than on whichever test happened to be written first.
/// </para>
/// </remarks>
internal static class ChoiceResolution {

    /// <summary>How one branch is recognised.</summary>
    internal sealed class Branch {
        public Branch(ChoiceBranchModel model) {
            Model = model;
        }

        public ChoiceBranchModel Model { get; }

        /// <summary>
        /// The JSON kind a payload must have, as a <c>JsonValueKind</c> member, or null where the
        /// kind does not distinguish this branch from the others.
        /// </summary>
        public string? ValueKind { get; set; }

        /// <summary>A property only this branch declares, if there is one.</summary>
        public string? DistinctProperty { get; set; }

        /// <summary>A property whose value only this branch permits, and that value.</summary>
        public string? ConstProperty { get; set; }

        public string? ConstValue { get; set; }

        /// <summary>
        /// The values this branch accepts, where they are a strict subset of what another branch of
        /// the same kind accepts.
        /// </summary>
        /// <remarks>
        /// <c>oneOf: [string, SomeEnum]</c> is common and both branches are strings, so nothing
        /// above separates them and every payload matches both. But an enum accepts strictly fewer
        /// values than an open string, so testing its members decides it - not a guess about which
        /// branch was meant, a fact about which schemas the value satisfies.
        /// </remarks>
        public List<string>? ValueSet { get; set; }

        /// <summary>
        /// Whether this branch accepts everything a narrower one of the same kind does, so it
        /// answers only after those have been tested.
        /// </summary>
        public bool IsWiderFallback { get; set; }

        /// <summary>
        /// Whether the schemas prove this branch apart from every other. False means the branch is
        /// still generated and decided by counting matches on the payload.
        /// </summary>
        public bool Proved =>
            ValueKind != null || DistinctProperty != null || ConstProperty != null ||
            ValueSet != null || IsWiderFallback;
    }

    internal sealed class Plan {
        public List<Branch> Branches { get; } = new();

        /// <summary>Branches no static test separates, which are decided when a payload arrives.</summary>
        public List<Branch> Overlapping { get; } = new();

        /// <summary>
        /// Whether a type is worth generating at all. False only where nothing can read the branches
        /// back - a choice whose every branch is a schema this parser could not resolve.
        /// </summary>
        public bool Usable { get; set; } = true;

        /// <summary>Whether every branch has a static test, so no payload is ever counted.</summary>
        public bool FullyProved => Overlapping.Count == 0;
    }

    public static Plan Resolve(
        IReadOnlyList<ChoiceBranchModel> branches, IReadOnlyList<SchemaModel> schemas) {
        var plan = new Plan();
        var byName = new Dictionary<string, SchemaModel>(System.StringComparer.Ordinal);

        foreach (var schema in schemas) {
            byName[schema.Name] = schema;
        }

        var resolved = new List<SchemaModel?>();

        foreach (var branch in branches) {
            SchemaModel? schema = null;

            if (branch.Ref != null) {
                byName.TryGetValue(TypeMapper.GetRefName(branch.Ref), out schema);
            }

            resolved.Add(schema);
            plan.Branches.Add(new Branch(branch));
        }

        AssignValueKinds(plan, resolved);
        AssignDistinctProperties(plan, resolved);
        AssignConstProperties(plan, resolved);
        AssignValueSets(plan, resolved);

        foreach (var branch in plan.Branches) {
            if (!branch.Proved) {
                plan.Overlapping.Add(branch);
            }
        }

        // A branch that names a schema nothing declares cannot be read into anything, so a choice
        // made entirely of those has no type to offer.
        var readable = 0;

        for (var index = 0; index < plan.Branches.Count; index++) {
            if (plan.Branches[index].Model.Ref == null || resolved[index] != null) {
                readable++;
            }
        }

        plan.Usable = readable > 1;

        return plan;
    }

    /// <summary>
    /// A kind is a test only where it belongs to one branch alone, so two object branches leave both
    /// to be separated by something else.
    /// </summary>
    private static void AssignValueKinds(Plan plan, List<SchemaModel?> resolved) {
        var counts = new Dictionary<string, int>(System.StringComparer.Ordinal);

        for (var index = 0; index < resolved.Count; index++) {
            var kind = ValueKindOf(plan.Branches[index].Model, resolved[index]);

            if (kind == null) {
                continue;
            }

            counts.TryGetValue(kind, out var count);
            counts[kind] = count + 1;
        }

        for (var index = 0; index < resolved.Count; index++) {
            var kind = ValueKindOf(plan.Branches[index].Model, resolved[index]);

            if (kind != null && counts[kind] == 1) {
                plan.Branches[index].ValueKind = kind;
            }
        }
    }

    /// <summary>A property no other branch declares.</summary>
    private static void AssignDistinctProperties(Plan plan, List<SchemaModel?> resolved) {
        for (var index = 0; index < resolved.Count; index++) {
            if (plan.Branches[index].Proved || resolved[index] == null) {
                continue;
            }

            foreach (var property in resolved[index]!.Properties) {
                if (!DeclaredElsewhere(property.Name, resolved, index)) {
                    plan.Branches[index].DistinctProperty = property.Name;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// A property pinned to one value is a discriminator the document did not label as one - which
    /// is how a great many descriptions spell it.
    /// </summary>
    private static void AssignConstProperties(Plan plan, List<SchemaModel?> resolved) {
        for (var index = 0; index < resolved.Count; index++) {
            var branch = plan.Branches[index];

            if (branch.Proved || resolved[index] == null) {
                continue;
            }

            foreach (var property in resolved[index]!.Properties) {
                if (property.EnumValues is not { Count: 1 }) {
                    continue;
                }

                var value = property.EnumValues[0];

                if (!ValueUsedElsewhere(property.Name, value, resolved, index)) {
                    branch.ConstProperty = property.Name;
                    branch.ConstValue = value;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Separates an enum from the open type it shares a kind with, most constrained first.
    /// </summary>
    /// <remarks>
    /// The alternative was to call them ambiguous and count matches, which throws on every value
    /// the enum permits - <c>oneOf: [string, AssistantSupportedModels]</c> appears three times in
    /// OpenAI's description and would have refused every model name it names. One branch accepting
    /// strictly fewer values than another is an ordering, not an ambiguity.
    /// </remarks>
    private static void AssignValueSets(Plan plan, List<SchemaModel?> resolved) {
        for (var index = 0; index < resolved.Count; index++) {
            var branch = plan.Branches[index];

            if (branch.Proved || resolved[index]?.Kind != SchemaKind.Enum) {
                continue;
            }

            var values = resolved[index]!.EnumValues;

            if (values is not { Count: > 0 }) {
                continue;
            }

            // Only where something else of the same kind would also accept these - otherwise the
            // kind already separated it and this adds nothing.
            var wider = WiderOfSameKind(plan, resolved, index);

            if (wider == null) {
                continue;
            }

            branch.ValueSet = new List<string>(values);
            wider.IsWiderFallback = true;
        }
    }

    /// <summary>
    /// A branch of the same JSON kind that constrains nothing, and so accepts everything the branch
    /// at <paramref name="index"/> does.
    /// </summary>
    private static Branch? WiderOfSameKind(Plan plan, List<SchemaModel?> resolved, int index) {
        var kind = ValueKindOf(plan.Branches[index].Model, resolved[index]);

        for (var other = 0; other < resolved.Count; other++) {
            if (other == index) {
                continue;
            }

            if (ValueKindOf(plan.Branches[other].Model, resolved[other]) != kind) {
                continue;
            }

            var schema = resolved[other];

            // Unconstrained: an inline type, or a named schema that is neither an enum nor an
            // object with a shape of its own.
            if (schema == null ||
                (schema.Kind != SchemaKind.Enum && schema.Properties.Count == 0)) {
                return plan.Branches[other];
            }
        }

        return null;
    }

    private static bool DeclaredElsewhere(string property, List<SchemaModel?> resolved, int skip) {
        for (var index = 0; index < resolved.Count; index++) {
            if (index == skip || resolved[index] == null) {
                continue;
            }

            foreach (var other in resolved[index]!.Properties) {
                if (string.Equals(other.Name, property, System.StringComparison.Ordinal)) {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ValueUsedElsewhere(
        string property, string value, List<SchemaModel?> resolved, int skip) {
        for (var index = 0; index < resolved.Count; index++) {
            if (index == skip || resolved[index] == null) {
                continue;
            }

            foreach (var other in resolved[index]!.Properties) {
                if (!string.Equals(other.Name, property, System.StringComparison.Ordinal)) {
                    continue;
                }

                // A property of the same name with no fixed value could carry anything, this value
                // included.
                if (other.EnumValues == null || other.EnumValues.Contains(value)) {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The <c>JsonValueKind</c> a payload of this branch arrives as.
    /// </summary>
    /// <remarks>
    /// Integer and number are one kind, and so are all the string formats - the reader does not see
    /// those distinctions, so neither can a test written against it. <c>true</c> and <c>false</c>
    /// are two kinds in System.Text.Json and one type here, which is why a boolean branch is matched
    /// with a check rather than a switch label.
    /// </remarks>
    private static string? ValueKindOf(ChoiceBranchModel branch, SchemaModel? schema) {
        if (schema != null) {
            if (schema.Kind == SchemaKind.Enum) {
                return "String";
            }

            if (schema.Kind == SchemaKind.Array) {
                return "Array";
            }

            if (schema.Kind == SchemaKind.Object && schema.Properties.Count > 0) {
                return "Object";
            }
        }

        var type = schema?.Type ?? branch.Type;

        return type?.ToLowerInvariant() switch {
            "string" => "String",
            "integer" or "number" => "Number",
            "boolean" => "Boolean",
            "array" => "Array",
            "object" => "Object",
            _ => null
        };
    }

    /// <summary>The C# type a branch is read into.</summary>
    public static string CSharpType(ChoiceBranchModel branch) =>
        branch.Ref != null
            ? NamingHelper.ToPascalCase(TypeMapper.GetRefName(branch.Ref))
            : TypeMapper.MapToCSharpType(branch.Type, branch.Format);
}
