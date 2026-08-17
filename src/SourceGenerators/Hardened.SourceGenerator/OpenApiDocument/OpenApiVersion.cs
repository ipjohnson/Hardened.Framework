namespace Hardened.SourceGenerator.OpenApiDocument;

/// <summary>
/// Which OpenAPI version the emitted document declares, from
/// <c>&lt;HardenedOpenApiVersion&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// The default is <see cref="V3_2"/>, decided on Kiota: the client story points consumers at the
/// served document and their own generator, and Kiota has parsed 3.2 since <c>v1.30.0</c>
/// (January 2026). Emitting an older document to suit tooling that does not need the concession
/// would mean being unable to describe handlers the application declares - streaming has no
/// spelling before 3.2.
/// </para>
/// <para>
/// Three versions rather than every published one. Each is here because something in the emitted
/// document differs across it; a version nothing branches on would be a knob that changes one
/// string and lies about the rest.
/// </para>
/// </remarks>
public enum OpenApiVersion {
    /// <summary>
    /// What the document declared before the property existed.
    /// </summary>
    /// <remarks>
    /// Kept because 3.0 is still what a good deal of tooling reads, and because the exclusive-bound
    /// spelling below is genuinely different rather than merely older.
    /// </remarks>
    V3_0,

    /// <summary>
    /// JSON Schema 2020-12 alignment, which changes how an exclusive bound is written.
    /// </summary>
    V3_1,

    /// <summary>
    /// Adds <c>itemSchema</c>, the only spelling in any version that can describe a streamed
    /// response. The default.
    /// </summary>
    V3_2
}

/// <summary>
/// Reading <c>&lt;HardenedOpenApiVersion&gt;</c>, and what each version changes.
/// </summary>
public static class OpenApiVersionFacts {

    /// <summary>The MSBuild property that selects it.</summary>
    public const string PropertyName = "HardenedOpenApiVersion";

    public const OpenApiVersion Default = OpenApiVersion.V3_2;

    /// <summary>
    /// Parses the property's value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An empty or absent value is <see cref="Default"/>. Anything else that is not recognised
    /// returns <c>null</c> and is reported as a build error rather than falling back - a
    /// <c>&lt;HardenedOpenApiVersion&gt;3.2&lt;/HardenedOpenApiVersion&gt;</c> that silently
    /// emitted 3.0.0 is exactly the class of defect this framework exists to remove, and the whole
    /// point of the property is that somebody's toolchain depends on the answer.
    /// </para>
    /// </remarks>
    public static OpenApiVersion? Parse(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return Default;
        }

        switch (value!.Trim()) {
            case "3.0.0":
            case "3.0":
                return OpenApiVersion.V3_0;

            case "3.1.0":
            case "3.1":
                return OpenApiVersion.V3_1;

            case "3.2.0":
            case "3.2":
                return OpenApiVersion.V3_2;

            default:
                return null;
        }
    }

    /// <summary>The string the document's <c>openapi</c> field carries.</summary>
    public static string VersionString(OpenApiVersion version) =>
        version switch {
            OpenApiVersion.V3_0 => "3.0.0",
            OpenApiVersion.V3_1 => "3.1.0",
            _ => "3.2.0"
        };

    /// <summary>
    /// Whether an exclusive bound is a number in its own right rather than a flag beside an
    /// inclusive one.
    /// </summary>
    /// <remarks>
    /// 3.0 writes <c>"minimum": 5, "exclusiveMinimum": true</c>; from 3.1 JSON Schema 2020-12
    /// writes <c>"exclusiveMinimum": 5</c> and no <c>minimum</c>. Emitting the 3.0 spelling under a
    /// 3.1 header is not a stylistic difference - a reader following the later draft sees
    /// <c>exclusiveMinimum: true</c>, which is not a number, and either rejects the schema or
    /// silently drops the bound.
    /// </remarks>
    public static bool ExclusiveBoundsAreNumeric(OpenApiVersion version) =>
        version != OpenApiVersion.V3_0;

    /// <summary>
    /// Whether the version can describe a streamed response at all.
    /// </summary>
    /// <remarks>
    /// <c>itemSchema</c> arrived in 3.2. Before it there is no way to say "many of these, one after
    /// another" - putting the item's schema under <c>schema</c> claims the response is one of them,
    /// which is what the document said before any of this and is a lie rather than an omission.
    /// </remarks>
    public static bool SupportsItemSchema(OpenApiVersion version) =>
        version == OpenApiVersion.V3_2;
}
