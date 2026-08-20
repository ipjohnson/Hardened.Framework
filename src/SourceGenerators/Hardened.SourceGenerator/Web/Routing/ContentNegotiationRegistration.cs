using System.Linq;
using CSharpAuthor;
using Hardened.SourceGenerator.Shared;

namespace Hardened.SourceGenerator.Web.Routing;

/// <summary>
/// The service-wide negotiation policy, emitted into the routing table's DI method.
/// </summary>
/// <remarks>
/// <para>
/// Read off the entry point, which is where a whole-service policy belongs and where
/// <c>[CaseInsensitiveRoutes]</c> already lives. Both routing table generators call this, so an
/// application says it the same way whether its routes came from a description or from attributes.
/// </para>
/// <para>
/// Emitted only when something asked for a mode other than the default. The runtime defaults to
/// strict on its own, so an application that says nothing carries no registration for this at all.
/// </para>
/// </remarks>
internal static class ContentNegotiationRegistration {

    /// <summary>The C# for the registration, or null when there is nothing to say.</summary>
    /// <param name="attributeModels">The entry point's attributes.</param>
    /// <param name="documentMode">
    /// <c>x-hardened-content-negotiation</c> from a description, or empty. The attribute wins where
    /// both are present: it is the more local statement, and an application mixing two descriptions
    /// that disagree needs one place to settle it.
    /// </param>
    public static string? Statement(
        IReadOnlyList<AttributeModel>? attributeModels, string documentMode) {
        var mode = FromAttribute(attributeModels) ?? FromDocument(documentMode);

        return mode == null
            ? null
            : "serviceCollection.AddSingleton<" +
              "global::Hardened.Requests.Abstract.Serializer.IContentNegotiationPolicy>(" +
              "new global::Hardened.Requests.Abstract.Serializer.ContentNegotiationPolicy(" +
              $"global::Hardened.Requests.Abstract.Serializer.ContentNegotiationMode.{mode}))";
    }

    private static string? FromAttribute(IReadOnlyList<AttributeModel>? attributeModels) {
        if (attributeModels == null) {
            return null;
        }

        foreach (var attribute in attributeModels) {
            if (!attribute.TypeDefinition.Name.StartsWith(
                    "ContentNegotiation", System.StringComparison.Ordinal)) {
                continue;
            }

            // The argument as written - "ContentNegotiationMode.Lenient" or just "Lenient".
            return attribute.Arguments.Contains("Lenient") ? "Lenient" : "Strict";
        }

        return null;
    }

    private static string? FromDocument(string documentMode) =>
        documentMode switch {
            "lenient" => "Lenient",
            "strict" => "Strict",
            _ => null
        };
}
