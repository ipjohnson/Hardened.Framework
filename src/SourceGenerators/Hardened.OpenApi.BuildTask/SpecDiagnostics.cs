using System.Collections.Generic;
using Hardened.OpenApi.SourceGenerator;
using Hardened.OpenApi.SourceGenerator.Models;

namespace Hardened.OpenApi.BuildTask;

/// <summary>
/// Problems a spec can describe that generate C# which will not compile.
/// </summary>
/// <remarks>
/// Reported against the spec file, because that is what the author edits. Left undetected these
/// surface as compiler errors in generated code under <c>obj/</c> - a file nobody can open and fix,
/// with a message that never mentions the document that caused it.
/// </remarks>
internal static class SpecDiagnostics {

    internal readonly struct Problem {
        public Problem(string code, string message) {
            Code = code;
            Message = message;
        }

        public string Code { get; }

        public string Message { get; }
    }

    public static IReadOnlyList<Problem> Find(OpenApiSpecModel model) {
        var problems = new List<Problem>();

        FindDuplicateSchemaNames(model, problems);

        foreach (var schema in model.Schemas) {
            var typeName = NamingHelper.ToPascalCase(schema.Name);

            foreach (var property in schema.Properties) {
                if (NamingHelper.ToPascalCase(property.Name) != typeName) {
                    continue;
                }

                // C# forbids it outright: CS0542, "member names cannot be the same as their
                // enclosing type". The emitted record would be
                // "record Message(string Message)", which is not a compilable declaration.
                //
                // Not worked around by renaming the property, though the wire name is now pinned by
                // [JsonPropertyName] and renaming would be invisible to clients. A generated model
                // whose property is called something other than what the author wrote is worse to
                // debug than being told to pick a different name, and this collides rarely enough
                // that asking is reasonable.
                problems.Add(new Problem(
                    "HOAT003",
                    $"Schema '{schema.Name}' declares property '{property.Name}', which generates a " +
                    $"member named '{typeName}' inside a type of the same name - C# does not allow " +
                    "that (CS0542). Rename either the schema or the property."));
            }
        }

        return problems;
    }

    /// <summary>
    /// Two schemas that would generate one C# type.
    /// </summary>
    /// <remarks>
    /// Reachable now that objects written inline are lifted into named schemas: <c>Pet</c> with an
    /// inline <c>address</c> synthesizes <c>PetAddress</c>, which a document is free to have
    /// declared already. Renaming one of them silently would give the author a public type they did
    /// not write and cannot find in their specification, so they are told instead.
    ///
    /// <para>
    /// Also catches two declared schemas whose names differ only in a way PascalCasing removes -
    /// <c>pet_address</c> and <c>petAddress</c> - which produced a duplicate type declaration that
    /// only surfaced as CS0101 in generated code.
    /// </para>
    /// </remarks>
    private static void FindDuplicateSchemaNames(OpenApiSpecModel model, List<Problem> problems) {
        var seen = new Dictionary<string, string>();

        foreach (var schema in model.Schemas) {
            var typeName = NamingHelper.ToPascalCase(schema.Name);

            if (seen.TryGetValue(typeName, out var first)) {
                problems.Add(new Problem(
                    "HOAT005",
                    $"Schemas '{first}' and '{schema.Name}' both generate a type named " +
                    $"'{typeName}'. One of them is a name given to a schema written inline; rename " +
                    "the declared schema or the property it collides with."));

                continue;
            }

            seen.Add(typeName, schema.Name);
        }
    }
}
