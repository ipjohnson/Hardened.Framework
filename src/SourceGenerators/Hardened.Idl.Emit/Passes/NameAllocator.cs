using System;
using System.Collections.Generic;
using System.Globalization;
using Hardened.Idl.Models;

namespace Hardened.Idl;

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
/// different one. It is asserted in the tests, and it is why nothing added to disambiguate carries a
/// separator - <c>_</c> would be stripped on the next pass and two names would converge again.
/// </para>
/// <para>
/// <b>A name that has to move is qualified by the scope that owns it</b> - <c>ZoomDateTime</c>,
/// <c>RepositoryClone</c>, <c>queryPath</c> - because that is the thing which distinguishes it from
/// whatever it collided with, and it reads. The first attempt was a hash of the same information,
/// which was equally stable and produced <c>DateTimeN9bec7490</c>; stability was never the part that
/// was hard. Only where the scope distinguishes nothing does a number appear, and sorting before
/// allocating is what makes which one gets it independent of the document's order.
/// </para>
/// </remarks>
internal static class NameAllocator {

    /// <summary>
    /// Members a record already has, which a property cannot redeclare.
    /// </summary>
    /// <remarks>
    /// <c>Clone</c> is the record-only one and is stricter than the rest: the compiler reserves the
    /// name for its copy method and rejects any member called it outright (CS8859), where the
    /// others merely collide. Bitbucket declares a property named <c>clone</c>.
    /// </remarks>
    private static readonly string[] ObjectMembers = {
        "ToString", "Equals", "GetHashCode", "GetType", "ReferenceEquals", "MemberwiseClone",
        "Clone", "Deconstruct", "PrintMembers", "EqualityContract"
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

        /// <param name="alternative">
        /// What to call it instead when the name it wants is taken. Every caller supplies the same
        /// kind of thing - the name qualified by the scope that contains it, so Bitbucket's
        /// <c>clone</c> becomes <c>RepositoryClone</c> and Zoom's <c>DateTime</c> becomes
        /// <c>ZoomDateTime</c>. Derived from the thing being named rather than from a counter, so
        /// the answer does not depend on how many names came before it.
        /// </param>
        public string Allocate(string desired, string alternative) {
            var candidate = NamingHelper.ToPascalCase(desired);

            if (_taken.Add(candidate)) {
                return candidate;
            }

            var qualified = NamingHelper.ToPascalCase(alternative);

            if (qualified != candidate && _taken.Add(qualified)) {
                return qualified;
            }

            // Both taken, which means two things the document does not distinguish by anything this
            // scope can see. Numbered, and the order is fixed by sorting before allocating.
            for (var suffix = 2; ; suffix++) {
                var numbered = candidate + suffix.ToString(CultureInfo.InvariantCulture);

                if (_taken.Add(numbered)) {
                    return numbered;
                }
            }
        }
    }

    /// <summary>
    /// A name qualified by the scope that contains it - or unchanged, where that would only stutter.
    /// </summary>
    /// <remarks>
    /// A schema called <c>StripeAccount</c> in <c>stripe.yaml</c> would become
    /// <c>StripeStripeAccount</c>, which is worse than the numbered form it falls through to. A name
    /// that <em>equals</em> its scope is the opposite case and does get qualified: GitHub's commit
    /// schema declares a property called <c>commit</c>, and <c>CommitCommit</c> at least says which
    /// two things met, where a number says nothing.
    /// </remarks>
    private static string Qualify(string scope, string name) {
        var prefix = NamingHelper.ToPascalCase(scope);
        var pascal = NamingHelper.ToPascalCase(name);

        return pascal.Length > prefix.Length && pascal.StartsWith(prefix, StringComparison.Ordinal)
            ? pascal
            : prefix + pascal;
    }

    public static void Apply(ServiceSpecModel model, string specFileName) {
        var file = NamingHelper.ToPascalCase(specFileName);

        AllocateTypeNames(model, file);
        AllocateOperationNames(model, file);
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
    private static void AllocateTypeNames(ServiceSpecModel model, string file) {
        var scope = new Scope(new[] {
            file + "Patterns", file + "Specification", file + "JsonTypeInfoResolver",

            // Exactly the names the type mapper resolves by spelling - no more. It looks a type up
            // by its rendered name, so a schema called DateTime became System.DateTime everywhere
            // it was referenced; Zoom declares one, and it is a date range with `from` and `to`,
            // not a moment. The keyword forms (int, string) cannot collide because a pascal-cased
            // name never produces one, and reserving ordinary words like Type or Object would
            // rename a great many schemas to no purpose.
            "DateTime", "DateOnly", "JsonElement"
        });

        var ordered = new List<SchemaModel>(model.Schemas);
        ordered.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));

        var renamed = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var schema in ordered) {
            // Qualified by the document, because what a schema collides with is either another
            // schema in the same document or a name the language already spends - and the document
            // is what distinguishes it from both.
            var allocated = scope.Allocate(schema.Name, Qualify(file, schema.Name));

            scope.Reserve(allocated + "Validator");

            // Same argument for a choice type: the converter is named after it and has nowhere
            // else to go. See OneOfConverterEmitter.
            if (schema.Kind == SchemaKind.OneOf) {
                scope.Reserve(allocated + "Converter");
            }

            if (allocated != schema.Name) {
                renamed[schema.Name] = allocated;
                schema.Name = allocated;
            }
        }

        if (renamed.Count > 0) {
            foreach (var reference in ModelRefs.All(model)) {
                var name = TypeMapper.GetRefName(reference.Value ?? "");

                if (reference.Value != null && renamed.TryGetValue(name, out var replacement)) {
                    reference.Set(TypeMapper.MakeRef(replacement));
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
    private static void AllocateOperationNames(ServiceSpecModel model, string file) {
        var services = new List<ServiceModel>(model.Services);
        services.Sort((left, right) => string.CompareOrdinal(left.Tag, right.Tag));

        var tags = new Scope();

        foreach (var service in services) {
            var tag = service.Tag ?? "Default";

            service.TypeBaseName = tags.Allocate(tag, Qualify(file, tag));
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
            // Not qualified by the tag: Cloudflare's DeleteWebhook and deleteWebhook share one, so
            // it distinguishes nothing. What differs is the route, so a colliding id falls back to
            // the name the operation would have had if it had declared none - deleteZonesZoneId -
            // which is a convention the generator already uses and a reader already recognises.
            operation.MethodName = ids.Allocate(
                operation.OperationId,
                NamingHelper.OperationIdFromRoute(operation.HttpMethod, operation.Path));
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

            var typeName = NamingHelper.ToPascalCase(schema.Name);

            foreach (var property in properties) {
                // Qualified by the type that declares it, which is what a property collides
                // against: its own type's name, a member every record already has, or another
                // property of the same type. Bitbucket's repository.clone becomes RepositoryClone.
                property.MemberNameOverride =
                    members.Allocate(property.Name, Qualify(typeName, property.Name));
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
                    allocated[index] = values.Allocate(
                        schema.EnumValues[index], Qualify(typeName, schema.EnumValues[index]));
                }

                schema.EnumMemberNames = new List<string>(allocated);
            }
        }

        foreach (var service in model.Services) {
            foreach (var operation in service.Operations) {
                var parameters = new Scope();

                foreach (var parameter in Ordered(operation.Parameters)) {
                    // Qualified by where it travels, because that is what OpenAPI allows two
                    // parameters of one name to differ by: Kubernetes' proxy routes take `path` in
                    // the path and `path` in the query, so the second becomes queryPath.
                    parameter.MemberNameOverride = NamingHelper.EscapeIdentifier(
                        NamingHelper.ToCamelCase(
                            parameters.Allocate(
                                parameter.Name,
                                Qualify(parameter.In ?? "value", parameter.Name))));
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
}

