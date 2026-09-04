using System;
using System.Collections.Generic;

namespace Hardened.Generation.Document;

/// <summary>
/// Rewrites a served document to declare an older OpenAPI version, for
/// <c>&lt;HardenedOpenApiOutputVersion&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// The served document declares 3.2 by default, which is the only version able to describe a
/// streamed response. A reader built on an older library refuses that banner - Spectral's
/// <c>oas</c> ruleset and NSwag's parser both do - and before this existed the only way to feed one
/// was to lower <c>&lt;HardenedOpenApiVersion&gt;</c> for the whole application, costing every
/// streaming operation its schema at the wire as well as in a file. This lowers the exported file
/// alone; what the application serves is untouched.
/// </para>
/// <para>
/// What changes, and only when the target version needs it. The <c>openapi</c> banner. Every
/// <c>itemSchema</c>, which arrived in 3.2 and is what <c>scripts/extract-openapi.py</c> removed
/// for the same reason; each operation that loses one is returned so the caller can name it. And,
/// for 3.0.0 only, the two spellings the generator itself writes differently under a 3.0 banner:
/// a numeric <c>exclusiveMinimum</c> or <c>exclusiveMaximum</c> becomes the bound plus a boolean,
/// and a <c>type</c> array of one type and <c>"null"</c> becomes the type with
/// <c>nullable: true</c>, because a 3.0 reader takes <c>type</c> as a string and nothing else.
/// </para>
/// </remarks>
internal static class OpenApiDocumentLowering {

    /// <summary>The versions the property accepts, as the file will declare them.</summary>
    public static readonly string[] AcceptedVersions = { "3.0.0", "3.1.0" };

    /// <summary>
    /// The three-part banner for a property value, or null when the value is not one the export
    /// can write.
    /// </summary>
    public static string? Normalise(string? version) {
        switch ((version ?? "").Trim()) {
            case "3.0":
            case "3.0.0":
                return "3.0.0";
            case "3.1":
            case "3.1.0":
                return "3.1.0";
            default:
                return null;
        }
    }

    /// <summary>
    /// Lowers <paramref name="document"/> in place to <paramref name="version"/>, which must be one
    /// of <see cref="AcceptedVersions"/>, and returns the operations whose streamed response lost
    /// its item schema, as <c>GET /events</c>.
    /// </summary>
    public static IReadOnlyList<string> Lower(JsonObject document, string version) {
        document.Set("openapi", new JsonString(version));

        var lost = new List<string>();

        if (document.Get("paths") is JsonObject paths) {
            foreach (var path in paths.Members) {
                if (!(path.Value is JsonObject operations)) {
                    continue;
                }

                foreach (var operation in operations.Members) {
                    if (operation.Value is JsonObject body && RemoveItemSchemas(body)) {
                        lost.Add(operation.Key.ToUpperInvariant() + " " + path.Key);
                    }
                }
            }
        }

        if (version == "3.0.0") {
            RewriteForThreeZero(document);
        }

        return lost;
    }

    /// <summary>Drops every <c>itemSchema</c> under an operation's responses.</summary>
    private static bool RemoveItemSchemas(JsonObject operation) {
        var removed = false;

        if (!(operation.Get("responses") is JsonObject responses)) {
            return false;
        }

        foreach (var response in responses.Members) {
            if (!(response.Value is JsonObject responseBody) || !(responseBody.Get("content") is JsonObject content)) {
                continue;
            }

            foreach (var mediaType in content.Members) {
                if (mediaType.Value is JsonObject media && media.Remove("itemSchema")) {
                    removed = true;
                }
            }
        }

        return removed;
    }

    /// <summary>
    /// The 3.0 spellings, applied to every object in the tree. Schemas appear under components,
    /// parameters, request bodies and responses alike, so this walks everything rather than
    /// knowing where a schema may sit.
    /// </summary>
    private static void RewriteForThreeZero(JsonNode node) {
        switch (node) {
            case JsonObject obj:
                RewriteExclusiveBound(obj, "exclusiveMinimum", "minimum");
                RewriteExclusiveBound(obj, "exclusiveMaximum", "maximum");
                RewriteNullableTypeArray(obj);

                foreach (var member in obj.Members) {
                    RewriteForThreeZero(member.Value);
                }

                break;
            case JsonArray array:
                foreach (var item in array.Items) {
                    RewriteForThreeZero(item);
                }

                break;
        }
    }

    /// <summary>
    /// <c>"exclusiveMinimum": 5</c> becomes <c>"minimum": 5, "exclusiveMinimum": true</c>, in the
    /// exclusive keyword's position so the order a reader sees is stable.
    /// </summary>
    private static void RewriteExclusiveBound(JsonObject schema, string exclusiveKey, string boundKey) {
        for (var index = 0; index < schema.Members.Count; index++) {
            var member = schema.Members[index];

            if (!string.Equals(member.Key, exclusiveKey, StringComparison.Ordinal) || !(member.Value is JsonNumber bound)) {
                continue;
            }

            schema.Members[index] = new KeyValuePair<string, JsonNode>(boundKey, bound);
            schema.Members.Insert(index + 1, new KeyValuePair<string, JsonNode>(exclusiveKey, JsonBoolean.True));

            return;
        }
    }

    /// <summary>
    /// <c>"type": ["string", "null"]</c> becomes <c>"type": "string", "nullable": true</c>.
    /// </summary>
    private static void RewriteNullableTypeArray(JsonObject schema) {
        for (var index = 0; index < schema.Members.Count; index++) {
            var member = schema.Members[index];

            if (!string.Equals(member.Key, "type", StringComparison.Ordinal) || !(member.Value is JsonArray types) || types.Items.Count != 2) {
                continue;
            }

            string? remaining = null;
            var sawNull = false;

            foreach (var item in types.Items) {
                if (item is JsonString text) {
                    if (text.Value == "null") {
                        sawNull = true;
                    }
                    else {
                        remaining = text.Value;
                    }
                }
            }

            if (!sawNull || remaining == null) {
                return;
            }

            schema.Members[index] = new KeyValuePair<string, JsonNode>("type", new JsonString(remaining));
            schema.Members.Insert(index + 1, new KeyValuePair<string, JsonNode>("nullable", JsonBoolean.True));

            return;
        }
    }
}
