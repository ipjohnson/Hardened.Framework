using System.Collections.Generic;
using CSharpAuthor;
using Hardened.Generation;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;

namespace Hardened.SourceGenerator.OpenApiDocument;

/// <summary>
/// The generated document, as a partial of the entry point carrying it gzip-compressed.
/// </summary>
/// <remarks>
/// <para>
/// Emitted only for an entry point that enabled <c>[Enable&lt;OpenApiDocumentPublishing&gt;]</c>, so an
/// application that does not serve a document does not carry one. It used to be written
/// unconditionally on the grounds that the cost was one string - which understated it, because a C#
/// string literal lives in the assembly's <c>#US</c> heap as UTF-16 and so costs two bytes per ASCII
/// character.
/// </para>
/// <para>
/// <b>Compressed, and as bytes rather than a string.</b> Measured on a 279,276 byte document, built
/// and sized three ways: a <c>static readonly string</c> costs 562,688 bytes of assembly, the same
/// document gzipped and base64'd into a string costs 92,672, and gzipped into a
/// <c>ReadOnlySpan&lt;byte&gt;</c> costs 37,376. The span wins twice over base64 because base64
/// gives back a third of what compression saved, and it wins over both because the C# compiler
/// lowers an all-constant <c>byte[]</c> initializer behind a <c>ReadOnlySpan&lt;byte&gt;</c> into a
/// metadata blob - no per-element IL, no allocation, and nothing copied at startup.
/// </para>
/// <para>
/// It is also the form the document is served in. Practically every client sends
/// <c>Accept-Encoding: gzip</c>, so the common path writes these bytes untouched and compresses
/// nothing at run time.
/// </para>
/// <para>
/// <b>Under a fixed name, because a build task reads it back.</b> The literal is the one copy of
/// the document the assembly carries, and <c>&lt;HardenedOpenApiOutput&gt;</c> writes it to a file
/// after the compile by opening the assembly with a metadata reader rather than by loading it or
/// by scraping generated source. The reader finds the getter by name: a static class named
/// <see cref="DocumentTypeName"/> nested in the entry point, with one static getter named
/// <see cref="DocumentPropertyName"/> whose body is the literal and nothing else. Both front ends
/// come through this method, so a code-first and a normalised spec-first document have the same
/// shape in the assembly. Nested in the entry point rather than declared at a fixed full name so
/// two entry points in one compilation each carry their own, and the task can say so rather than
/// the compiler reporting a duplicate type. <c>WriteOpenApiDocument</c> in
/// <c>Hardened.OpenApiDocument.BuildTask</c> is the other half; change one name and change both.
/// </para>
/// <para>
/// <b>Determinism.</b> Incremental generation and reproducible builds both require identical output
/// for identical input, and a compressor that stamped a timestamp would break both.
/// <c>GZipStream</c> writes MTIME as zero, which is what makes this safe;
/// <c>OpenApiDocumentSourceTests</c> asserts it rather than trusting it.
/// </para>
/// </remarks>
internal static class OpenApiDocumentSource {

    public static string Write(
        EntryPointSelector.Model appModel, IReadOnlyList<RequestHandlerModel> handlers, string basePath,
        OpenApiVersion version = OpenApiVersionFacts.Default, DocumentIdentity? identity = null) {
        var file = new CSharpFileDefinition(appModel.EntryPointType.Namespace);

        var entryPoint = file.AddClass(appModel.EntryPointType.Name);

        entryPoint.Modifiers |= ComponentModifier.Public | ComponentModifier.Partial;

        var document = OpenApiDocumentGenerator.Write(appModel, handlers, basePath, version, identity);

        var container = entryPoint.AddClass(DocumentTypeName);

        container.Modifiers |= ComponentModifier.Public | ComponentModifier.Static;
        container.Comment =
            "The OpenAPI document this application serves, as the build wrote it. " +
            "<HardenedOpenApiOutput> reads it from the compiled assembly under this name.";

        var property = container.AddProperty(ReadOnlySpanOfByte, DocumentPropertyName);

        property.Modifiers |= ComponentModifier.Public | ComponentModifier.Static;
        property.Set = null;
        property.Get.LambdaSyntax = true;
        property.Get.AddCode(GZipLiteral.Write(document));
        property.Comment =
            "The routes this application declares, as a gzip-compressed OpenAPI "
            + OpenApiVersionFacts.VersionString(version) + " document.";

        var outputContext = new OutputContext(new OutputContextOptions {
            TypeOutputMode = TypeOutputMode.Global
        });

        file.WriteOutput(outputContext);

        return outputContext.Output();
    }

    /// <summary>
    /// The static class nested in the entry point that holds the literal. The build task that
    /// exports the document looks for a nested type of this name.
    /// </summary>
    public const string DocumentTypeName = "OpenApiDocument";

    /// <summary>
    /// The getter on <see cref="DocumentTypeName"/>. What the routing generator registers, and what
    /// the build task decodes.
    /// </summary>
    public const string DocumentPropertyName = "GZip";

    /// <summary>
    /// How the routing table names the literal from the entry point's own scope:
    /// <c>OpenApiDocument.GZip</c>.
    /// </summary>
    public const string DocumentMemberPath = DocumentTypeName + "." + DocumentPropertyName;

    private static ITypeDefinition ReadOnlySpanOfByte =>
        new GenericTypeDefinition(
            TypeDefinitionEnum.ClassDefinition, "System", "ReadOnlySpan",
            new[] { TypeDefinition.Get(typeof(byte)) });
}
