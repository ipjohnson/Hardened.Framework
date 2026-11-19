using System;
using System.Collections.Generic;
using Hardened.Generation;
using Hardened.Generation.Models;
using Hardened.Idl.Emitters;

namespace Hardened.Idl;

/// <summary>
/// The schemas earlier descriptions of one project have already generated, so a later one that
/// declares a schema alike uses that type rather than writing a second.
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
/// One instance per task run, fed the descriptions in item order. The first file to declare a
/// schema writes it. <c>ExtractSpecTask</c> and the generator tests' harness both drive it, so the
/// harness compiles what a build compiles.
/// </para>
/// </remarks>
internal sealed class SharedSchemas
{
    private readonly Dictionary<string, Declaration> _declared = new(StringComparer.Ordinal);

    /// <summary>
    /// Which of <paramref name="model"/>'s schemas an earlier file already generated, and which it
    /// declares differently.
    /// </summary>
    /// <remarks>
    /// A file with a conflict claims nothing, because it will not be written. A later file that
    /// declares one of its other schemas then writes it, rather than relying on a type nothing
    /// emitted.
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
                        System.Globalization.CultureInfo.InvariantCulture,
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

        if (conflicts.Count == 0)
        {
            foreach (var entry in claimed)
            {
                _declared.Add(entry.Key, entry.Value);
            }
        }

        return new SchemaClaim(emittedElsewhere, conflicts);
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
    public SchemaClaim(ISet<string> emittedElsewhere, IReadOnlyList<string> conflicts)
    {
        EmittedElsewhere = emittedElsewhere;
        Conflicts = conflicts;
    }

    /// <summary>The schemas to leave out of this file, because an earlier file wrote them alike.</summary>
    public ISet<string> EmittedElsewhere { get; }

    /// <summary>One message per schema this file declares differently from an earlier file.</summary>
    public IReadOnlyList<string> Conflicts { get; }
}
