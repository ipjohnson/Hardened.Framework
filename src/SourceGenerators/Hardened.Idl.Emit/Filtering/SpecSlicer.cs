using System;
using System.Collections.Generic;
using Hardened.Idl;
using Hardened.Idl.Models;

namespace Hardened.Idl.Filtering;

/// <summary>
/// The part of a description one service implements.
/// </summary>
/// <remarks>
/// <para>
/// A published description is not a service. GitHub ships one document covering some six hundred
/// operations across dozens of domains, and nobody implements that as one application - they
/// implement repositories, or issues, and glue them together at the root path. Generating the whole
/// document produced 42.8 MB of source and exceeded the metadata limit on user strings, which is
/// the symptom; the cause is that the unit of generation was the file rather than the service.
/// </para>
/// <para>
/// Selecting operations is the easy half. The schemas are where the size is, so what is kept is the
/// transitive closure of what the surviving operations actually reach - through properties, array
/// elements, dictionary values, base types and discriminator branches - and everything else goes.
/// </para>
/// <para>
/// The closure runs whether or not a filter was given. A description declares component schemas, it
/// does not promise anything uses them: Zoom declares a <c>DateTime</c> object that nothing in the
/// document references, and generating it produced a type that collided with the BCL name for no
/// benefit at all. A filter changes which operations are roots of the closure; it is not what
/// decides that there is one.
/// </para>
/// </remarks>
internal static class SpecSlicer {

    /// <param name="IncludePaths">Path globs to keep. Empty keeps every path.</param>
    /// <param name="ExcludePaths">Path globs to drop, applied after the include set.</param>
    /// <param name="Tags">Tags to keep. Empty keeps every tag.</param>
    internal sealed class Filter {
        public IReadOnlyList<string> IncludePaths { get; set; } = System.Array.Empty<string>();

        public IReadOnlyList<string> ExcludePaths { get; set; } = System.Array.Empty<string>();

        public IReadOnlyList<string> Tags { get; set; } = System.Array.Empty<string>();

        public bool IsEmpty =>
            IncludePaths.Count == 0 && ExcludePaths.Count == 0 && Tags.Count == 0;
    }

    internal sealed class Result {
        public int OperationsKept { get; set; }

        public int OperationsDropped { get; set; }

        public int SchemasKept { get; set; }

        public int SchemasDropped { get; set; }

        /// <summary>
        /// References a surviving operation or schema makes to something the slice removed.
        /// </summary>
        /// <remarks>
        /// The closure should make this impossible, and that is exactly why it is checked: a hole
        /// in it would not fail the build. A reference to a missing schema degrades to
        /// <c>JsonElement</c> further down, silently, so a slice that dropped too much would look
        /// like it worked and hand back a weaker model than the document describes.
        /// </remarks>
        public List<string> DanglingReferences { get; } = new();

        /// <summary>
        /// Whether the filter selected nothing at all.
        /// </summary>
        /// <remarks>
        /// The failure worth catching. A mistyped glob removes every operation, and the build then
        /// succeeds against an empty project - no error, no types, and nothing to suggest the
        /// filter was the cause.
        /// </remarks>
        public bool MatchedNothing { get; set; }
    }

    /// <param name="keepUnreferenced">
    /// Whether schemas nothing reaches are emitted anyway. The escape hatch for a project that
    /// hand-writes calls against types the description declares but never uses in an operation.
    /// </param>
    public static Result Apply(ServiceSpecModel model, Filter filter, bool keepUnreferenced = false) {
        var result = new Result();

        // 1. Operations the filter selects. An empty filter selects them all - it is only the
        // roots of the closure below that a filter changes, not whether there is one.
        if (!filter.IsEmpty) {
            foreach (var service in model.Services) {
                var kept = new List<OperationModel>();

                foreach (var operation in service.Operations) {
                    if (Selected(operation, service.Tag, filter)) {
                        kept.Add(operation);
                    } else {
                        result.OperationsDropped++;
                    }
                }

                service.Operations = kept;
            }

            model.Services.RemoveAll(service => service.Operations.Count == 0);
        }

        result.OperationsKept = CountOperations(model);

        if (keepUnreferenced) {
            result.SchemasKept = model.Schemas.Count;
            result.MatchedNothing = !filter.IsEmpty && result.OperationsKept == 0;

            return result;
        }

        // 2. Schemas those operations reach, transitively.
        var byName = new Dictionary<string, SchemaModel>(StringComparer.Ordinal);

        foreach (var schema in model.Schemas) {
            byName[schema.Name] = schema;
        }

        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();

        void Reach(string? reference) {
            if (reference == null) {
                return;
            }

            var name = TypeMapper.GetRefName(reference);

            // A name the document itself never declares is not the slice's doing; the parser has
            // already degraded those.
            if (byName.ContainsKey(name) && reachable.Add(name)) {
                pending.Push(name);
            }
        }

        foreach (var service in model.Services) {
            foreach (var operation in service.Operations) {
                Reach(operation.RequestBodyRef);
                Reach(operation.ResponseRef);
                Reach(operation.ResponseArrayItemsRef);

                foreach (var error in operation.ErrorResponses) {
                    Reach(error.Ref);
                }

                foreach (var parameter in operation.Parameters) {
                    Reach(parameter.Ref);
                    Reach(parameter.ArrayItemsRef);
                }
            }
        }

        // Derived types, indexed by the base they extend.
        //
        // Every other edge here runs from a use to what it names, and a derived type is the one
        // thing nothing names: a response typed as Pet is answered on the wire by a Dog, and the
        // only trace of Dog is Dog's own allOf pointing back at Pet. Followed only where the base
        // declares a discriminator, which is the document saying the substitution happens; a plain
        // allOf is reuse, and a derived type nothing references really is unreachable.
        var derived = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var schema in model.Schemas) {
            if (schema.BaseRef == null) {
                continue;
            }

            var baseName = TypeMapper.GetRefName(schema.BaseRef);

            if (!derived.TryGetValue(baseName, out var subtypes)) {
                derived[baseName] = subtypes = new List<string>();
            }

            subtypes.Add(schema.Name);
        }

        while (pending.Count > 0) {
            var schema = byName[pending.Pop()];

            Reach(schema.BaseRef);
            Reach(schema.ArrayItemsRef);

            foreach (var mapping in schema.DiscriminatorMapping) {
                Reach(mapping.Ref);
            }

            if (!string.IsNullOrEmpty(schema.DiscriminatorPropertyName) &&
                derived.TryGetValue(schema.Name, out var subtypes)) {
                foreach (var subtype in subtypes) {
                    Reach(TypeMapper.MakeRef(subtype));
                }
            }

            foreach (var property in schema.Properties) {
                Reach(property.Ref);
                Reach(property.ArrayItemsRef);
                Reach(property.DictionaryValueRef);

                // The property is typed JsonElement, so nothing emitted names these - but they are
                // what the payload is allowed to be, and a caller deserializing into one needs the
                // type to exist.
                foreach (var branch in property.OneOf) {
                    Reach(branch.Ref);
                }
            }
        }

        var dropped = new HashSet<string>(StringComparer.Ordinal);

        foreach (var schema in model.Schemas) {
            if (!reachable.Contains(schema.Name)) {
                dropped.Add(schema.Name);
            }
        }

        result.SchemasDropped = model.Schemas.RemoveAll(schema => dropped.Contains(schema.Name));
        result.SchemasKept = model.Schemas.Count;
        result.MatchedNothing = !filter.IsEmpty && result.OperationsKept == 0;

        VerifyClosure(model, dropped, result);

        return result;
    }

    /// <summary>
    /// That nothing kept still points at something removed.
    /// </summary>
    private static void VerifyClosure(
        ServiceSpecModel model, HashSet<string> dropped, Result result) {
        if (dropped.Count == 0) {
            return;
        }

        void Check(string? reference, string from) {
            if (reference != null && dropped.Contains(TypeMapper.GetRefName(reference))) {
                result.DanglingReferences.Add(
                    from + " references '" + TypeMapper.GetRefName(reference) + "'");
            }
        }

        foreach (var service in model.Services) {
            foreach (var operation in service.Operations) {
                var from = operation.HttpMethod + " " + operation.Path;

                Check(operation.RequestBodyRef, from);
                Check(operation.ResponseRef, from);
                Check(operation.ResponseArrayItemsRef, from);

                foreach (var error in operation.ErrorResponses) {
                    Check(error.Ref, from);
                }

                foreach (var parameter in operation.Parameters) {
                    Check(parameter.Ref, from);
                    Check(parameter.ArrayItemsRef, from);
                }
            }
        }

        foreach (var schema in model.Schemas) {
            var from = "schema '" + schema.Name + "'";

            Check(schema.BaseRef, from);
            Check(schema.ArrayItemsRef, from);

            foreach (var property in schema.Properties) {
                Check(property.Ref, from);
                Check(property.ArrayItemsRef, from);
                Check(property.DictionaryValueRef, from);
            }
        }
    }

    private static int CountOperations(ServiceSpecModel model) {
        var count = 0;

        foreach (var service in model.Services) {
            count += service.Operations.Count;
        }

        return count;
    }

    private static bool Selected(OperationModel operation, string? tag, Filter filter) {
        if (filter.Tags.Count > 0 && !Matches(filter.Tags, tag ?? operation.Tag ?? "")) {
            return false;
        }

        if (filter.IncludePaths.Count > 0 && !MatchesAnyGlob(filter.IncludePaths, operation.Path)) {
            return false;
        }

        return !MatchesAnyGlob(filter.ExcludePaths, operation.Path);
    }

    private static bool Matches(IReadOnlyList<string> candidates, string value) {
        foreach (var candidate in candidates) {
            if (string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesAnyGlob(IReadOnlyList<string> globs, string path) {
        foreach (var glob in globs) {
            if (GlobMatches(glob, path)) {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A path glob, over URL segments.
    /// </summary>
    /// <remarks>
    /// <c>*</c> matches within one segment and <c>**</c> matches any number of them, which is the
    /// shape people already write for file globs and the shape Kiota uses for this same job. Path
    /// parameters are ordinary text here: <c>/repos/*/issues</c> matches
    /// <c>/repos/{owner}/issues</c>.
    /// </remarks>
    internal static bool GlobMatches(string glob, string path) {
        var globParts = Split(glob);
        var pathParts = Split(path);

        return MatchFrom(globParts, 0, pathParts, 0);
    }

    private static string[] Split(string value) =>
        value.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

    private static bool MatchFrom(string[] glob, int g, string[] path, int p) {
        while (g < glob.Length) {
            if (glob[g] == "**") {
                // Trailing ** matches whatever is left, including nothing.
                if (g == glob.Length - 1) {
                    return true;
                }

                for (var skip = p; skip <= path.Length; skip++) {
                    if (MatchFrom(glob, g + 1, path, skip)) {
                        return true;
                    }
                }

                return false;
            }

            if (p >= path.Length || !SegmentMatches(glob[g], path[p])) {
                return false;
            }

            g++;
            p++;
        }

        return p == path.Length;
    }

    private static bool SegmentMatches(string glob, string segment) {
        if (glob == "*") {
            return true;
        }

        if (glob.IndexOf('*') < 0) {
            return string.Equals(glob, segment, StringComparison.OrdinalIgnoreCase);
        }

        // One wildcard run at a time, anchored at both ends.
        var parts = glob.Split('*');
        var index = 0;

        for (var i = 0; i < parts.Length; i++) {
            var part = parts[i];

            if (part.Length == 0) {
                continue;
            }

            if (i == 0) {
                if (!segment.StartsWith(part, StringComparison.OrdinalIgnoreCase)) return false;
                index = part.Length;
                continue;
            }

            var found = segment.IndexOf(part, index, StringComparison.OrdinalIgnoreCase);

            if (found < 0) {
                return false;
            }

            index = found + part.Length;
        }

        return parts[parts.Length - 1].Length == 0 ||
               segment.EndsWith(parts[parts.Length - 1], StringComparison.OrdinalIgnoreCase);
    }
}
