using Hardened.Generation;
using System.Collections.Generic;
using Hardened.Generation.Models;

namespace Hardened.Idl;

/// <summary>
/// Problems a spec can describe that generate C# which will not compile.
/// </summary>
/// <remarks>
/// <para>
/// Reported against the spec file, because that is what the author edits. Left undetected these
/// surface as compiler errors in generated code under <c>obj/</c> - a file nobody can open and fix,
/// with a message that never mentions the document that caused it.
/// </para>
/// <para>
/// Codes are the front end's prefix plus 020-025 and 027, one number per finder. The prefix is a
/// parameter because this pass runs for every front end and a finding belongs to the document
/// that caused it: a Smithy model's mixed enum reported as HOAT anything sends its author to the
/// OpenAPI documentation. 020 up is this pass's block; everything below it belongs to the task
/// shell, the packaged targets and the Smithy CLI task, per docs/generator-diagnostics.md.
/// </para>
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

    /// <summary>
    /// A <c>oneOf</c> that could not become a type, and why.
    /// </summary>
    /// <remarks>
    /// The property lands on <c>JsonElement</c>, which works and is the weakest thing that does -
    /// the caller gets unparsed JSON and no help deciding what is in it. Worth saying out loud
    /// rather than leaving to be discovered: the fix is a discriminator in the document, or
    /// ShapeMatchOneOf if the branches really can be told apart by shape.
    /// </remarks>
    private static void FindUnresolvableChoices(
        ServiceSpecModel model, string prefix, List<Problem> problems) {
        foreach (var schema in model.Schemas) {
            if (schema.Kind != SchemaKind.OneOf || schema.DiscriminatorPropertyName != null) {
                continue;
            }

            var plan = ChoiceResolution.Resolve(schema.OneOf, model.Schemas);

            if (plan.FullyProved) {
                continue;
            }

            var names = new List<string>();

            foreach (var branch in plan.Overlapping) {
                names.Add(ChoiceResolution.CSharpType(branch.Model));
            }

            problems.Add(new Problem(
                prefix + "022",
                $"'{schema.Name}' declares no discriminator and nothing in the schemas separates " +
                $"{string.Join(" from ", names)}, so those are told apart by reading the payload " +
                "into each and requiring exactly one to fit. A payload matching several is an " +
                "error at that point. Declaring a discriminator would decide it here instead.",
                fatal: false));
        }
    }

    /// <summary>
    /// Keywords the description declared and the parser did not map.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A warning, not an error. The application is correct in every other respect and refusing to
    /// build it would be worse than the omission; what it must not do is accept the keyword in
    /// silence, which is what happened for every keyword in this class until now. A caller reading
    /// the description sees <c>multipleOf: 5</c> and expects 3 to be refused, and nothing anywhere
    /// said otherwise.
    /// </para>
    /// <para>
    /// <b>One warning per distinct keyword, not per site.</b> A large description declaring
    /// <c>uniqueItems</c> on forty arrays has one thing wrong with it and forty messages would bury
    /// it - the same failure as a Smithy CLI error emitting one MSBuild line per line of output.
    /// The count and a representative location carry what the extra thirty-nine would have.
    /// </para>
    /// <para>
    /// This does not catch a keyword that was read and then flattened. That is a different class,
    /// found by auditing the parser rather than by asking the model, and the difference is worth
    /// keeping straight: nothing here would have found <c>summary</c> being folded into
    /// <c>description</c>, because the parser mapped it.
    /// </para>
    /// </remarks>
    private static void FindUnmappedKeywords(
        ServiceSpecModel model, string prefix, List<Problem> problems) {
        if (model.UnmappedKeywords.Count == 0) {
            return;
        }

        var byKeyword = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var order = new List<string>();

        // Deduped on keyword and location together, because a parser may meet the same member more
        // than once and one member is one place however many times it was read. Smithy's does: an
        // operation's input structure is walked for the schema and walked again for the request
        // body's properties, so every member of it is built twice. Counting that as two sites would
        // put "and 1 other place" on a message about one, which is worse than not counting at all.
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var unmapped in model.UnmappedKeywords) {
            if (!seen.Add(unmapped.Keyword + "\u001f" + unmapped.Location)) {
                continue;
            }

            if (!byKeyword.TryGetValue(unmapped.Keyword, out var locations)) {
                locations = new List<string>();
                byKeyword[unmapped.Keyword] = locations;
                order.Add(unmapped.Keyword);
            }

            locations.Add(unmapped.Location);
        }

        // Declared order rather than alphabetical, so the first message names the first thing the
        // author would find reading their own document top to bottom.
        foreach (var keyword in order) {
            var locations = byKeyword[keyword];

            var where = locations.Count == 1
                ? $"at {locations[0]}"
                : $"at {locations[0]} and {locations.Count - 1} other " +
                  (locations.Count == 2 ? "place" : "places");

            problems.Add(new Problem(
                prefix + "024",
                $"'{keyword}' is declared {where} and is not enforced. The description promises it " +
                "and the generated application does not apply it, so a payload this rejects on " +
                "paper is accepted at runtime. Remove it, or keep it and enforce the rule in the " +
                "handler.",
                // Explicitly, because the constructor's default is fatal. An application declaring
                // a keyword this does not honour is otherwise correct, and refusing to build it
                // would be a worse answer than the omission it is reporting.
                fatal: false));
        }
    }

    /// <summary>
    /// An <c>enum</c> declaring both strings and numbers, which is not a C# enum in either form.
    /// </summary>
    /// <remarks>
    /// Fatal rather than resolved. Honouring the strings puts the numbers out of reach and honouring
    /// the numbers puts the strings out of reach, so either choice silently drops half the values a
    /// caller may send - which is the shape of defect the whole enum vocabulary work exists to
    /// close. The document has to say which it means.
    /// </remarks>
    private static void FindMixedEnums(
        ServiceSpecModel model, string prefix, List<Problem> problems) {
        foreach (var schema in model.Schemas) {
            if (schema.Kind == SchemaKind.Enum && schema.Type == MixedEnumType) {
                problems.Add(new Problem(
                    prefix + "023",
                    $"Enum '{schema.Name}' declares both string and numeric values. A C# enum " +
                    "carries one wire form or the other, and picking one here would put every " +
                    "value of the other kind out of reach. Declare the members as all strings or " +
                    "all numbers.",
                    fatal: true));
            }
        }
    }

    /// <summary>
    /// What the parser marks a mixed enum's type as. Spelled here rather than referenced, because
    /// this assembly does not see the OpenAPI parser.
    /// </summary>
    internal const string MixedEnumType = "mixed-enum";

    /// <summary>
    /// References the description makes to something it never declares.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Fatal, unlike everything else in the 020 block that resolves to a workable answer. There is
    /// no answer here: a dangling reference in a response degraded the success case to a bodyless
    /// one, so a handler written against the generated interface compiled and answered 200 with an
    /// empty body. The only errors were CS0246s a hop away in application code that happened to
    /// name the missing model, or nothing at all.
    /// </para>
    /// <para>
    /// One report per reference rather than per name: a document referencing a schema it dropped
    /// usually does so from several places, and each is a separate edit.
    /// </para>
    /// </remarks>
    private static void FindDanglingReferences(
        ServiceSpecModel model, string prefix, List<Problem> problems) {
        foreach (var dangling in model.DanglingReferences) {
            problems.Add(new Problem(
                prefix + "027",
                $"'{dangling.Location}' references '{dangling.Reference}', which the description " +
                "does not declare. Nothing is generated for it, so the member it types would be " +
                "absent and a response body would be dropped. Declare the schema, or point the " +
                "reference at one that exists."));
        }
    }

    public static IReadOnlyList<Problem> Find(ServiceSpecModel model, string diagnosticPrefix) {
        var problems = new List<Problem>();

        FindDanglingReferences(model, diagnosticPrefix, problems);
        FindDuplicateSchemaNames(model, diagnosticPrefix, problems);
        FindUnresolvableChoices(model, diagnosticPrefix, problems);
        FindMixedEnums(model, diagnosticPrefix, problems);
        FindUnmappedKeywords(model, diagnosticPrefix, problems);
        FindCollidingErrorStatuses(model, diagnosticPrefix, problems);

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
                    diagnosticPrefix + "020",
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
    /// <summary>
    /// Two declared errors sharing one status on one operation.
    /// </summary>
    /// <remarks>
    /// The case type is named for the operation and the status, so two errors at 409 emit the
    /// same record twice - six compiler errors in generated code, none of which names the model.
    /// A valid model can say this (Smithy binds a status to the error shape, so two shapes can
    /// carry the same code), which is exactly why it has to be reported here: smithy validate
    /// accepts it and the compiler garbles it.
    /// </remarks>
    private static void FindCollidingErrorStatuses(
        ServiceSpecModel model, string prefix, List<Problem> problems) {
        foreach (var service in model.Services) {
            foreach (var operation in service.Operations) {
                var seen = new Dictionary<int, string?>();

                foreach (var error in operation.ErrorResponses) {
                    if (seen.TryGetValue(error.StatusCode, out var first)) {
                        problems.Add(new Problem(
                            prefix + "025",
                            $"operation '{operation.OperationId}' declares two error responses " +
                            $"at status {error.StatusCode} ('{first}' and '{error.Ref}'). One " +
                            "status has one case type, so this generates the same record twice. " +
                            "Give the errors distinct statuses, or merge them into one shape " +
                            "carrying both bodies."));
                    } else {
                        seen[error.StatusCode] = error.Ref;
                    }
                }
            }
        }
    }

    private static void FindDuplicateSchemaNames(
        ServiceSpecModel model, string prefix, List<Problem> problems) {
        var seen = new Dictionary<string, string>();

        foreach (var schema in model.Schemas) {
            var typeName = NamingHelper.ToPascalCase(schema.Name);

            if (seen.TryGetValue(typeName, out var first)) {
                // Both kinds of collision are resolved before this runs - synthesized names are
                // made unique as they are invented, declared ones are renamed afterwards. This is
                // the assertion that neither missed, and it does not stop the build, because a
                // duplicate type name surfaces immediately as CS0101 anyway.
                problems.Add(new Problem(
                    prefix + "021",
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
