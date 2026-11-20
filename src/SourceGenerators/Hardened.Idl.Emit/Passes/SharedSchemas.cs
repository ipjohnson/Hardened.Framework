using System;
using System.Collections.Generic;
using System.Globalization;
using Hardened.Generation;
using Hardened.Generation.Models;
using Hardened.Idl.Emitters;

namespace Hardened.Idl;

/// <summary>
/// The schemas and error types earlier descriptions of one project have already generated, so a
/// later one that declares one alike uses that type rather than writing a second.
/// </summary>
/// <remarks>
/// <para>
/// A project that splits its contract into one file per resource keeps each file a valid document
/// alone, so each declares the shared shapes it uses. Every file's types land in one models
/// namespace, and two records of one name there are CS0579 and CS8863. Only the record collides:
/// each file's JSON type info resolver is named after its file, so each keeps its own entry for the
/// shared type.
/// </para>
/// <para>
/// "Alike" is the schema's record in the serialized model, which is everything the generator half
/// reads, plus whether the file streams it. A streamed union is emitted with an event form, so a
/// file that streams one and a file that does not want different types under one name.
/// </para>
/// <para>
/// A named response is the same case. Each file that uses <c>components/responses/NotFound</c>
/// generates its exception, its response-set case and its <c>AsException()</c> factory, whether the
/// file declares the response or a <c>$ref</c> copied it in from another file. Those are claimed by
/// the generated type's name, and "alike" is the error's record without the names
/// <c>NameAllocator</c> gave it.
/// </para>
/// <para>
/// One instance per task run, fed the descriptions in item order. The first file to declare a
/// type writes it. <c>ExtractSpecTask</c> and the generator tests' harness both drive it, so the
/// harness compiles what a build compiles.
/// </para>
/// </remarks>
internal sealed class SharedSchemas
{
    private readonly Dictionary<string, Declaration> _declared = new(StringComparer.Ordinal);

    private readonly Dictionary<string, Declaration> _errorTypes = new(StringComparer.Ordinal);

    /// <summary>Each payload's factory, by the exception it throws.</summary>
    private readonly Dictionary<string, string> _factories = new(StringComparer.Ordinal);

    /// <summary>
    /// Which of <paramref name="model"/>'s types an earlier file already generated, and which it
    /// declares differently.
    /// </summary>
    /// <remarks>
    /// A file with a conflict claims nothing, because it will not be written. A later file that
    /// declares one of its other types then writes it, rather than relying on a type nothing
    /// emitted. Reads <c>model.ResponseModel</c>, which decides which error types the file writes.
    /// </remarks>
    public SchemaClaim Claim(ServiceSpecModel model, string specPath)
    {
        var streamed = SpecFileEmitter.StreamedItems(model);
        var emittedElsewhere = new HashSet<string>(StringComparer.Ordinal);
        var conflicts = new List<string>();
        var claimed = new List<KeyValuePair<string, Declaration>>();

        foreach (var schema in model.Schemas)
        {
            var form =
                SpecModelSerializer.Write(schema)
                + (streamed.Contains(NamingHelper.ToPascalCase(schema.Name)) ? "streamed\n" : "");

            if (!_declared.TryGetValue(schema.Name, out var earlier))
            {
                claimed.Add(
                    new KeyValuePair<string, Declaration>(
                        schema.Name,
                        new Declaration(form, specPath)
                    )
                );
            }
            else if (string.Equals(earlier.Form, form, StringComparison.Ordinal))
            {
                emittedElsewhere.Add(schema.Name);
            }
            else
            {
                conflicts.Add(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Schema '{0}' is declared in '{1}' and in '{2}', and the two differ. "
                            + "Every description of a project generates into one models "
                            + "namespace, so a name declared in two of them has to be declared "
                            + "alike, or renamed in one.",
                        schema.Name,
                        earlier.SpecPath,
                        specPath
                    )
                );
            }
        }

        var errors = new ErrorClaim(this, specPath, conflicts);
        var thrown = SpecFileEmitter.GeneratedErrors(model, model.ResponseModel, false);

        foreach (var error in thrown)
        {
            errors.Claim(error.ExceptionTypeName!, "exception", error);
        }

        foreach (var error in SpecFileEmitter.GeneratedErrors(model, model.ResponseModel, true))
        {
            errors.Claim(error.TypeName!, "case", error);
        }

        var factoriesElsewhere = new HashSet<string>(StringComparer.Ordinal);
        var factoriesClaimed = new List<KeyValuePair<string, string>>();

        foreach (var factory in ErrorFactoryEmitter.Factories(thrown))
        {
            var exception = factory.Value.ExceptionTypeName!;

            if (!_factories.TryGetValue(factory.Key, out var earlier))
            {
                factoriesClaimed.Add(new KeyValuePair<string, string>(factory.Key, exception));
            }
            else if (string.Equals(earlier, exception, StringComparison.Ordinal))
            {
                factoriesElsewhere.Add(factory.Key);
            }
        }

        if (conflicts.Count == 0)
        {
            foreach (var entry in claimed)
            {
                _declared.Add(entry.Key, entry.Value);
            }

            foreach (var entry in errors.Claimed)
            {
                _errorTypes.Add(entry.Key, entry.Value);
            }

            foreach (var entry in factoriesClaimed)
            {
                _factories.Add(entry.Key, entry.Value);
            }
        }

        return new SchemaClaim(
            emittedElsewhere,
            conflicts,
            errors.EmittedElsewhere,
            factoriesElsewhere
        );
    }

    /// <summary>One file's error types, against what earlier files wrote.</summary>
    private sealed class ErrorClaim
    {
        private readonly SharedSchemas _shared;
        private readonly string _specPath;
        private readonly List<string> _conflicts;

        public ErrorClaim(SharedSchemas shared, string specPath, List<string> conflicts)
        {
            _shared = shared;
            _specPath = specPath;
            _conflicts = conflicts;
        }

        public HashSet<string> EmittedElsewhere { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, Declaration> Claimed { get; } = new(StringComparer.Ordinal);

        public void Claim(string typeName, string kind, ErrorResponseModel error)
        {
            var form = kind + "\n" + SpecModelSerializer.WriteDeclaration(error);

            if (!_shared._errorTypes.TryGetValue(typeName, out var earlier))
            {
                Claimed[typeName] = new Declaration(form, _specPath);
            }
            else if (string.Equals(earlier.Form, form, StringComparison.Ordinal))
            {
                EmittedElsewhere.Add(typeName);
            }
            else
            {
                _conflicts.Add(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "The {0} response '{1}' generates '{2}' in '{3}' and in '{4}', and the two "
                            + "declarations differ. Every description of a project generates into "
                            + "one models namespace, so a response declared in two of them has to "
                            + "be declared alike, or renamed in one.",
                        error.StatusCode,
                        error.Name ?? ShippedResponses.GeneratedName(error),
                        typeName,
                        earlier.SpecPath,
                        _specPath
                    )
                );
            }
        }
    }

    private sealed class Declaration
    {
        public Declaration(string form, string specPath)
        {
            Form = form;
            SpecPath = specPath;
        }

        public string Form { get; }

        public string SpecPath { get; }
    }
}

/// <summary>What <see cref="SharedSchemas.Claim"/> decided for one description.</summary>
internal sealed class SchemaClaim
{
    public SchemaClaim(
        ISet<string> emittedElsewhere,
        IReadOnlyList<string> conflicts,
        ISet<string>? errorTypesEmittedElsewhere = null,
        ISet<string>? factoriesEmittedElsewhere = null
    )
    {
        EmittedElsewhere = emittedElsewhere;
        Conflicts = conflicts;
        ErrorTypesEmittedElsewhere =
            errorTypesEmittedElsewhere ?? new HashSet<string>(StringComparer.Ordinal);
        FactoriesEmittedElsewhere =
            factoriesEmittedElsewhere ?? new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>The schemas to leave out of this file, because an earlier file wrote them alike.</summary>
    public ISet<string> EmittedElsewhere { get; }

    /// <summary>
    /// The exceptions and response-set cases to leave out of this file, by type name, because an
    /// earlier file wrote them alike.
    /// </summary>
    public ISet<string> ErrorTypesEmittedElsewhere { get; }

    /// <summary>
    /// The payloads whose <c>AsException()</c> an earlier file wrote for the same exception.
    /// </summary>
    public ISet<string> FactoriesEmittedElsewhere { get; }

    /// <summary>One message per type this file declares differently from an earlier file.</summary>
    public IReadOnlyList<string> Conflicts { get; }
}
