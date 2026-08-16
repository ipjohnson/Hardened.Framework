using System;
using System.Collections.Generic;
using System.Globalization;
using Hardened.Idl;
using Hardened.Idl.Models;

namespace Hardened.OpenApi.SourceGenerator;

/// <summary>
/// Every C# name this document produces, decided once.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> Names used to be derived at the point of use, from the raw spec string,
/// in some forty places - each emitter PascalCasing what it needed. Nothing owned the answer, so a
/// collision fixed in one place did not help the others, and each new document found whichever
/// caller had not been patched. Ten separate collisions were fixed that way before it was clear
/// they were one missing component: Stripe's synthesized names, Sentry's synthesized-against-
/// declared, Ory's NullTime beside nullTime, Sentry again on Monitor beside MonitorValidator,
/// Kubernetes' two parameters called path, GitHub's property named after its own type, Elastic's
/// two properties and its enum members, Jira's toString, Cloudflare's DeleteWebhook beside
/// deleteWebhook.
/// </para>
/// <para>
/// <b>The scopes are fixed by C#, not by documents.</b> A name has to be unique within its
/// namespace, within its type, within its enum, or within an operation's parameters - and there is
/// no fifth kind, because those are the containers the language defines. A document cannot invent a
/// new one, which is what makes this closable rather than something to keep extending.
/// </para>
/// <para>
/// <b>Allocated names are idempotent under <see cref="NamingHelper.ToPascalCase"/>.</b> That is the
/// invariant that lets the emitters keep deriving: passing an allocated name through the sanitizer
/// again returns it unchanged, so a call site that re-derives gets the same answer rather than a
/// different one. It is asserted below, and it is why a disambiguating suffix carries no separator -
/// <c>_</c> would be stripped on the next pass and two names would converge again.
/// </para>
/// </remarks>
internal static class NameAllocator {

    /// <summary>Members every type already has, which a property cannot redeclare.</summary>
    private static readonly string[] ObjectMembers = {
        "ToString", "Equals", "GetHashCode", "GetType", "ReferenceEquals", "MemberwiseClone"
    };

    /// <summary>
    /// One container's names.
    /// </summary>
    private sealed class Scope {
        private readonly HashSet<string> _taken = new(StringComparer.Ordinal);

        public Scope(IEnumerable<string>? reserved = null) {
            if (reserved == null) {
                return;
            }

            foreach (var name in reserved) {
                _taken.Add(name);
            }
        }

        public void Reserve(string name) => _taken.Add(name);

        /// <param name="provenance">
        /// What makes this name different from the one it collided with. Disambiguation is derived
        /// from it rather than from a counter, so the answer depends on the thing being named and
        /// not on how many names were allocated before it - a document that gains a schema does not
        /// rename the ones it already had.
        /// </param>
        public string Allocate(string desired, string provenance) {
            var candidate = NamingHelper.ToPascalCase(desired);

            if (_taken.Add(candidate)) {
                return candidate;
            }

            candidate += Suffix(provenance);

            // Only if the provenance itself collides, which means two identical things.
            while (!_taken.Add(candidate)) {
                candidate += "X";
            }

            return candidate;
        }
    }

    public static void Apply(ServiceSpecModel model, string specFileName) {
        AllocateTypeNames(model, specFileName);
        AllocateOperationNames(model);
        AllocateMemberNames(model);
    }

    /// <summary>
    /// Schema type names, and the helper names derived from them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sorted, so which of two colliding schemas keeps the plain name does not depend on the order
    /// the document happened to list them in.
    /// </para>
    /// <para>
    /// Each allocation also reserves <c>{name}Validator</c>, because the validation generator names
    /// a validator after the type it checks - Sentry declares both <c>Monitor</c> and
    /// <c>MonitorValidator</c>, and the generated one has nowhere else to go. Reserving it as the
    /// type is allocated works because a name always sorts before itself plus a suffix, so
    /// <c>Monitor</c> is always allocated first and always wins the argument.
    /// </para>
    /// </remarks>
    private static void AllocateTypeNames(ServiceSpecModel model, string specFileName) {
        var file = NamingHelper.ToPascalCase(specFileName);

        var scope = new Scope(new[] {
            file + "Patterns", file + "Specification", file + "JsonTypeInfoResolver"
        });

        var ordered = new List<SchemaModel>(model.Schemas);
        ordered.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));

        var renamed = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var schema in ordered) {
            var allocated = scope.Allocate(schema.Name, schema.Name);

            scope.Reserve(allocated + "Validator");

            if (allocated != schema.Name) {
                renamed[schema.Name] = allocated;
                schema.Name = allocated;
            }
        }

        if (renamed.Count > 0) {
            foreach (var reference in ModelRefs.All(model)) {
                var name = TypeMapper.GetRefName(reference.Value ?? "");

                if (reference.Value != null && renamed.TryGetValue(name, out var replacement)) {
                    reference.Set("#/components/schemas/" + replacement);
                }
            }
        }
    }

    /// <summary>
    /// Operation ids and service tags, which name types as well as methods.
    /// </summary>
    /// <remarks>
    /// An operation id becomes a method, a handler class and an <c>I…Parameters</c> interface; a tag
    /// becomes an <c>I…Service</c> interface and a controller. Cloudflare declares
    /// <c>DeleteWebhook</c> and <c>deleteWebhook</c>, which is one of each declared twice - and
    /// because the parameter interface is partial, the two merged into a single type with every
    /// member declared twice rather than failing where the duplication was.
    /// </remarks>
    private static void AllocateOperationNames(ServiceSpecModel model) {
        var services = new List<ServiceModel>(model.Services);
        services.Sort((left, right) => string.CompareOrdinal(left.Tag, right.Tag));

        var tags = new Scope();

        foreach (var service in services) {
            service.TypeBaseName = tags.Allocate(service.Tag ?? "Default", service.Tag ?? "Default");
        }

        var operations = new List<OperationModel>();

        foreach (var service in services) {
            operations.AddRange(service.Operations);
        }

        operations.Sort((left, right) => {
            var byId = string.CompareOrdinal(left.OperationId, right.OperationId);
            return byId != 0
                ? byId
                : string.CompareOrdinal(left.HttpMethod + left.Path, right.HttpMethod + right.Path);
        });

        var ids = new Scope();

        foreach (var operation in operations) {
            operation.MethodName = ids.Allocate(
                operation.OperationId, operation.HttpMethod + " " + operation.Path);
        }
    }

    /// <summary>
    /// Members, each within the type or enum or operation that contains them.
    /// </summary>
    /// <remarks>
    /// The wire name never moves - <c>[JsonPropertyName]</c> pins it - so only the C# member is
    /// allocated, and it is carried on the model rather than re-derived by each emitter.
    /// </remarks>
    private static void AllocateMemberNames(ServiceSpecModel model) {
        foreach (var schema in model.Schemas) {
            // The type's own name is taken: C# forbids a member matching its enclosing type
            // (CS0542), which GitHub's commit.commit and Stripe's error.error both are.
            var members = new Scope(ObjectMembers) { };

            members.Reserve(NamingHelper.ToPascalCase(schema.Name));

            // Allocated in sorted order, emitted in document order. Which of two properties that
            // reach C# as one name keeps the plain one must not depend on how the document happened
            // to list them - Commit declares `name` beside `Name`, and a vendor reordering their
            // file would otherwise rename a member out from under a consumer.
            var properties = new List<PropertyModel>(schema.Properties);

            properties.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));

            foreach (var property in properties) {
                property.MemberNameOverride = members.Allocate(property.Name, property.Name);
            }

            if (schema.EnumValues is { Count: > 0 }) {
                var values = new Scope();
                var allocated = new string[schema.EnumValues.Count];
                var order = new List<int>(schema.EnumValues.Count);

                for (var i = 0; i < schema.EnumValues.Count; i++) {
                    order.Add(i);
                }

                // Same rule, and the results go back to the positions their values hold, because
                // EnumMemberNames pairs with EnumValues by index.
                order.Sort((left, right) =>
                    string.CompareOrdinal(schema.EnumValues[left], schema.EnumValues[right]));

                foreach (var index in order) {
                    allocated[index] =
                        values.Allocate(schema.EnumValues[index], schema.EnumValues[index]);
                }

                schema.EnumMemberNames = new List<string>(allocated);
            }
        }

        foreach (var service in model.Services) {
            foreach (var operation in service.Operations) {
                var parameters = new Scope();

                foreach (var parameter in Ordered(operation.Parameters)) {
                    parameter.MemberNameOverride = NamingHelper.EscapeIdentifier(
                        NamingHelper.ToCamelCase(
                            parameters.Allocate(parameter.Name, parameter.In ?? "value")));
                }
            }
        }
    }

    /// <summary>
    /// Parameters in a fixed order, so which of two of one name keeps it is not the document's
    /// choice.
    /// </summary>
    /// <remarks>
    /// OpenAPI scopes a parameter's uniqueness to its name <em>and</em> its location, so one
    /// operation may legally declare two called the same thing - Kubernetes' proxy routes take
    /// <c>path</c> in the path and <c>path</c> in the query. The location decides, and it decides
    /// the same way every time.
    /// </remarks>
    private static List<ParameterModel> Ordered(List<ParameterModel> parameters) {
        static int Rank(string? location) => location switch {
            "path" => 0,
            "query" => 1,
            "header" => 2,
            "cookie" => 3,
            _ => 4
        };

        var ordered = new List<ParameterModel>(parameters);

        ordered.Sort((left, right) => {
            var byRank = Rank(left.In).CompareTo(Rank(right.In));
            return byRank != 0 ? byRank : string.CompareOrdinal(left.Name, right.Name);
        });

        return ordered;
    }

    /// <summary>
    /// A short, stable qualifier, carrying no separator so the result survives being sanitized
    /// again.
    /// </summary>
    /// <remarks>
    /// FNV-1a rather than <see cref="string.GetHashCode"/>, which is randomised per process in .NET
    /// Core - a generated type that renames itself on every build churns the file and recompiles
    /// every consumer.
    /// </remarks>
    private static string Suffix(string provenance) {
        unchecked {
            var hash = 2166136261;

            foreach (var character in provenance) {
                hash = (hash ^ character) * 16777619;
            }

            return "N" + hash.ToString("x8", CultureInfo.InvariantCulture);
        }
    }
}
