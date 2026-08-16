using System.Collections.Generic;
using Hardened.Idl.Models;

namespace Hardened.Idl;

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
        public Problem(string code, string message, bool fatal = true) {
            Code = code;
            Message = message;
            Fatal = fatal;
        }

        public string Code { get; }

        public string Message { get; }

        /// <summary>
        /// Whether the build stops. False where the generator has already chosen an answer and is
        /// only reporting it.
        /// </summary>
        /// <remarks>
        /// Both problems here used to be fatal, and both told the author to rename something in the
        /// document. That is reasonable advice for a specification you own and impossible for one
        /// you fetched: renaming a schema in GitHub's 9.4 MB description, and again on every
        /// update, is not a workflow. The generator resolves them and says what it did.
        /// </remarks>
        public bool Fatal { get; }
    }

    public static IReadOnlyList<Problem> Find(ServiceSpecModel model) {
        var problems = new List<Problem>();

        FindDuplicateSchemaNames(model, problems);

        foreach (var schema in model.Schemas) {
            var typeName = NamingHelper.ToPascalCase(schema.Name);

            foreach (var property in schema.Properties) {
                // The parser has already renamed the member; compare against the wire name to see
                // whether it had to.
                if (NamingHelper.ToPascalCase(property.Name) != typeName) {
                    continue;
                }

                // C# forbids it outright: CS0542, "member names cannot be the same as their
                // enclosing type". The emitted record would be
                // "record Message(string Message)", which is not a compilable declaration.
                problems.Add(new Problem(
                    "HOAT003",
                    $"Schema '{schema.Name}' declares property '{property.Name}', which would " +
                    $"generate a member named '{typeName}' inside a type of the same name - C# does " +
                    $"not allow that (CS0542). The member is generated as '{property.MemberName}'; " +
                    "the wire name is unchanged.",
                    fatal: false));
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
    private static void FindDuplicateSchemaNames(ServiceSpecModel model, List<Problem> problems) {
        var seen = new Dictionary<string, string>();

        foreach (var schema in model.Schemas) {
            var typeName = NamingHelper.ToPascalCase(schema.Name);

            if (seen.TryGetValue(typeName, out var first)) {
                // Both kinds of collision are resolved before this runs - synthesized names are
                // made unique as they are invented, declared ones are renamed afterwards. This is
                // the assertion that neither missed, and it does not stop the build, because a
                // duplicate type name surfaces immediately as CS0101 anyway.
                problems.Add(new Problem(
                    "HOAT005",
                    $"Schemas '{first}' and '{schema.Name}' both generate a type named " +
                    $"'{typeName}', which should have been resolved automatically. Rename one of " +
                    "them in the document.",
                    fatal: false));

                continue;
            }

            seen.Add(typeName, schema.Name);
        }
    }
}
