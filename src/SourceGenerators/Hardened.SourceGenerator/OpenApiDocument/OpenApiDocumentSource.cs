using System.Collections.Generic;
using CSharpAuthor;
using Hardened.Idl;
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
/// <b>Determinism.</b> Incremental generation and reproducible builds both require identical output
/// for identical input, and a compressor that stamped a timestamp would break both.
/// <c>GZipStream</c> writes MTIME as zero, which is what makes this safe;
/// <c>OpenApiDocumentSourceTests</c> asserts it rather than trusting it.
/// </para>
/// </remarks>
internal static class OpenApiDocumentSource {

    public static string Write(
        EntryPointSelector.Model appModel, IReadOnlyList<RequestHandlerModel> handlers, string basePath,
        OpenApiVersion version = OpenApiVersionFacts.Default) {
        var file = new CSharpFileDefinition(appModel.EntryPointType.Namespace);

        var entryPoint = file.AddClass(appModel.EntryPointType.Name);

        entryPoint.Modifiers |= ComponentModifier.Public | ComponentModifier.Partial;

        var document = OpenApiDocumentGenerator.Write(appModel, handlers, basePath, version);

        var property = entryPoint.AddProperty(ReadOnlySpanOfByte, DocumentPropertyName);

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

    /// <summary>What the routing generator registers, and what the extraction script looks for.</summary>
    public const string DocumentPropertyName = "OpenApiDocumentGZip";

    private static ITypeDefinition ReadOnlySpanOfByte =>
        new GenericTypeDefinition(
            TypeDefinitionEnum.ClassDefinition, "System", "ReadOnlySpan",
            new[] { TypeDefinition.Get(typeof(byte)) });
}
