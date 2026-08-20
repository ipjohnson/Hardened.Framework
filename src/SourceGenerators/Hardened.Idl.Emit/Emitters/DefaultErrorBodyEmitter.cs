using System.Collections.Generic;
using System.Linq;
using CSharpAuthor;
using Hardened.Idl.Models;
using Hardened.Idl;

namespace Hardened.Idl.Emitters;

/// <summary>
/// Writes the instances <see cref="DefaultErrorBody"/> decided on.
/// </summary>
/// <remarks>
/// Only the writing is here. What may go in one, and whether one can exist at all, is
/// <see cref="DefaultErrorBody"/> in the shared assembly - because the Roslyn generator that names
/// these fields runs in a different process and never sees this file, so both have to reach the same
/// answer from the specification model rather than from each other.
/// </remarks>
internal static class DefaultErrorBodyEmitter {

    /// <summary>
    /// Emits one field per distinct (schema, status) pair that can be filled.
    /// </summary>
    public static void Emit(
        IConstructContainer container,
        IReadOnlyList<SchemaModel> schemas,
        IReadOnlyCollection<(string SchemaName, int StatusCode)> wanted,
        string modelsNamespace) {
        if (wanted.Count == 0) {
            return;
        }

        ClassDefinition? holder = null;

        // Ordered, so the emitted file is byte-stable between builds whatever order the operations
        // were walked in.
        foreach (var pair in wanted
                     .OrderBy(candidate => candidate.SchemaName, System.StringComparer.Ordinal)
                     .ThenBy(candidate => candidate.StatusCode)) {
            var arguments = DefaultErrorBody.Arguments(schemas, pair.SchemaName, pair.StatusCode);

            if (arguments == null) {
                continue;
            }

            var schema = DefaultErrorBody.Find(schemas, pair.SchemaName);

            if (schema == null) {
                continue;
            }

            holder ??= CreateHolder(container);

            var typeName = NamingHelper.ToPascalCase(schema.Name);

            var field = holder.AddField(
                TypeDefinition.Get(modelsNamespace, typeName),
                DefaultErrorBody.FieldName(schema.Name, pair.StatusCode));

            field.Modifiers =
                ComponentModifier.Public | ComponentModifier.Static | ComponentModifier.Readonly;
            field.InitializeValue = new CodeOutputComponent(
                $"new global::{modelsNamespace}.{typeName}({string.Join(", ", arguments)})") {
                Indented = false
            };
            field.Comment = DocComment.Format(
                $"The body a null return writes for {pair.StatusCode}. Holds the status and its " +
                "reason phrase; nothing about the request that produced it.");
        }
    }

    private static ClassDefinition CreateHolder(IConstructContainer container) {
        var holder = container.AddClass(DefaultErrorBody.HolderTypeName);

        holder.Modifiers |= ComponentModifier.Public | ComponentModifier.Static;
        holder.Comment = DocComment.Format(
            "Bodies a handler's null return writes, one per declared status. Allocated once for " +
            "the life of the process, and serialized through the generated resolver like any " +
            "other response.");

        return holder;
    }
}
