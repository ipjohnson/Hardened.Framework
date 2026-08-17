using System.Collections.Generic;
using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;

namespace Hardened.SourceGenerator.OpenApiDocument;

/// <summary>
/// The generated document, as a partial of the entry point carrying it as a constant.
/// </summary>
/// <remarks>
/// Emitted for every application whether or not it serves the document, because the cost is a
/// string and having it available is what lets a build publish the contract without running the
/// application.
/// </remarks>
internal static class OpenApiDocumentSource {

    public static string Write(
        EntryPointSelector.Model appModel, IReadOnlyList<RequestHandlerModel> handlers, string basePath,
        OpenApiVersion version = OpenApiVersionFacts.Default) {
        var file = new CSharpFileDefinition(appModel.EntryPointType.Namespace);

        var entryPoint = file.AddClass(appModel.EntryPointType.Name);

        entryPoint.Modifiers |= ComponentModifier.Public | ComponentModifier.Partial;

        var field = entryPoint.AddField(typeof(string), "OpenApiDocument");

        field.Modifiers |= ComponentModifier.Public | ComponentModifier.Static | ComponentModifier.Readonly;
        field.Comment =
            "The routes this application declares, as an OpenAPI "
            + OpenApiVersionFacts.VersionString(version) + " document.";
        field.InitializeValue = new CodeOutputComponent(
            Quote(OpenApiDocumentGenerator.Write(appModel, handlers, basePath, version))) { Indented = false };

        var outputContext = new OutputContext(new OutputContextOptions {
            TypeOutputMode = TypeOutputMode.Global
        });

        file.WriteOutput(outputContext);

        return outputContext.Output();
    }

    private static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
