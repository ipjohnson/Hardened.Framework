using System.Linq;
using CSharpAuthor;
using Hardened.SourceGenerator.Shared;

namespace Hardened.SourceGenerator.Web.Routing;

/// <summary>
/// The service-wide error-body policy, emitted into the routing table's DI method.
/// </summary>
/// <remarks>
/// <para>
/// The same shape as <see cref="ContentNegotiationRegistration"/> and for the same reasons: read
/// off the entry point, where a whole-service policy belongs, and emitted by both routing table
/// generators so an application says it the same way whether its routes came from a description or
/// from attributes.
/// </para>
/// <para>
/// Emitted only where something asked for a format other than the default, so an application that
/// says nothing carries no registration for this and behaves exactly as it did.
/// </para>
/// </remarks>
internal static class ErrorBodyRegistration {

    /// <summary>The C# for the registration, or null when there is nothing to say.</summary>
    /// <param name="attributeModels">The entry point's attributes.</param>
    /// <param name="documentFormat">
    /// <c>x-hardened-error-bodies</c> from a description, or empty. The attribute wins where both
    /// are present, as it does for the negotiation policy.
    /// </param>
    public static string? Statement(
        IReadOnlyList<AttributeModel>? attributeModels, string documentFormat) {
        var format = FromAttribute(attributeModels) ?? FromDocument(documentFormat);

        return format == null
            ? null
            : "serviceCollection.AddSingleton<" +
              "global::Hardened.Requests.Abstract.Serializer.IErrorBodyPolicy>(" +
              "new global::Hardened.Requests.Abstract.Serializer.ErrorBodyPolicy(" +
              $"global::Hardened.Requests.Abstract.Serializer.ErrorBodyFormat.{format}))";
    }

    private static string? FromAttribute(IReadOnlyList<AttributeModel>? attributeModels) {
        if (attributeModels == null) {
            return null;
        }

        foreach (var attribute in attributeModels) {
            // A marker, so its presence is the whole statement.
            if (attribute.TypeDefinition.Name.StartsWith(
                    "JsonErrorBodies", System.StringComparison.Ordinal)) {
                return "Json";
            }
        }

        return null;
    }

    private static string? FromDocument(string documentFormat) =>
        documentFormat switch {
            "json" => "Json",
            "negotiated" => "Negotiated",
            _ => null
        };
}
