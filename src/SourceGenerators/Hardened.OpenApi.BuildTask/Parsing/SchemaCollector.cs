using System;
using System.Collections.Generic;
using Hardened.Idl;
using Hardened.Idl.Models;

namespace Hardened.OpenApi.SourceGenerator;

/// <summary>
/// Schemas lifted out of the places they were written inline, and every name already spoken for.
/// </summary>
/// <remarks>
/// <para>
/// A synthesized name has to be unique against the whole document, not just against other
/// synthesized names. Sentry declares <c>ReplayDeletionJobCreateData</c> and also has an inline
/// schema that synthesizes to the same thing; checking only the lifted ones let the two collide and
/// then reported them as "two declared schemas", which they are not.
/// </para>
/// <para>
/// It cannot be fixed afterwards. Both schemas end up registered under one
/// <c>#/components/schemas/</c> name, so every reference to either is the same string, and nothing
/// downstream can tell which was meant. Uniqueness has to hold at the moment a name is invented.
/// </para>
/// <para>
/// Names are compared PascalCased, because that is what reaches C#: <c>pet_address</c> and
/// <c>petAddress</c> are two names in the document and one type here.
/// </para>
/// </remarks>
internal sealed class SchemaCollector {

    private readonly List<SchemaModel> _synthesized = new();

    private readonly HashSet<string> _taken = new(StringComparer.Ordinal);

    /// <param name="declared">
    /// The document's own schema names, reserved before anything is parsed - an inline object
    /// inside a declared schema is lifted while that schema is still being read, so seeding this
    /// afterwards would be too late.
    /// </param>
    public SchemaCollector(IEnumerable<string>? declared) {
        if (declared == null) {
            return;
        }

        foreach (var name in declared) {
            _taken.Add(NamingHelper.ToPascalCase(name));
        }
    }

    public IReadOnlyList<SchemaModel> Synthesized => _synthesized;

    public bool IsTaken(string pascalName) => _taken.Contains(pascalName);

    /// <summary>Claims a name before its children are parsed, so they cannot take it.</summary>
    public void Reserve(string pascalName) => _taken.Add(pascalName);

    public void Add(SchemaModel model) {
        _synthesized.Add(model);
        _taken.Add(NamingHelper.ToPascalCase(model.Name));
    }
}
