using System.Collections.Generic;
using System.Threading;
using Hardened.SourceGenerator.Models.Request;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.Requests;

/// <summary>
/// What a handler's authorization attributes declare, for the published document.
/// </summary>
/// <remarks>
/// <para>
/// Enforcement never reads this. The pipeline honours <c>IAuthorizeAttribute</c> instances at run
/// time, attribute bodies and all; the document is written at compile time and can read only what
/// is literally in the source - a scheme named as <c>[Authorize&lt;TAuth&gt;]</c>'s type argument,
/// grants named as <c>[AuthorizeGrants("...")]</c>'s literal arguments. A derived grants
/// attribute or an <c>IGrantProvider</c> computes its grants in a constructor the generator
/// cannot execute, so those operations publish "authenticated via the scheme" and nothing more -
/// the same boundary <c>[Throws&lt;T&gt;]</c> lives with, stated rather than guessed at.
/// </para>
/// <para>
/// Using a scheme is declaring it: every distinct <c>TAuth</c> found on a handler or its
/// controller reaches <c>components.securitySchemes</c>, keyed by the type's name, with its
/// shape read from the scheme type's own attribute. Grants become the requirement's scope list
/// only when the scheme kind can carry one - OAuth2 - mirroring the rule the OpenAPI reader
/// applies in the other direction. Grants with no scheme in sight publish nothing, because a
/// requirement must reference a declared scheme.
/// </para>
/// </remarks>
internal static class SecurityDeclarationSelector {

    private const string AuthorizeAttributeName =
        "Hardened.Requests.Runtime.Authorization.AuthorizeAttribute";

    private const string GrantsAttributeName =
        "Hardened.Requests.Runtime.Authorization.AuthorizeGrantsAttribute";

    private const string SchemeInterface =
        "Hardened.Requests.Abstract.Authorization.IAuthenticationScheme";

    /// <summary>
    /// Reads the method's and its controller's attributes and writes what they declare onto the
    /// model.
    /// </summary>
    public static void Apply(
        GeneratorSyntaxContext context,
        MethodDeclarationSyntax method,
        RequestHandlerModel model,
        CancellationToken cancellationToken) {
        var schemes = new List<SecuritySchemeDeclaration>();
        var grants = new List<string>();
        var misplaced = new List<string>();

        Read(context, method.AttributeLists,
            model.ControllerType.Name + "." + model.HandlerMethod,
            schemes, grants, misplaced, cancellationToken);

        if (method.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault()
            is { } controller) {
            Read(context, controller.AttributeLists,
                model.ControllerType.Name,
                schemes, grants, misplaced, cancellationToken);
        }

        if (misplaced.Count > 0) {
            model.MisplacedSchemeAttributes = misplaced;
        }

        if (schemes.Count == 0) {
            return;
        }

        var requirements = new List<string>();

        foreach (var scheme in schemes) {
            requirements.Add(RequirementJson(scheme, grants));
        }

        model.DeclaredSecuritySchemes = schemes;
        model.SecurityRequirements = requirements;
    }

    private static void Read(
        GeneratorSyntaxContext context,
        SyntaxList<AttributeListSyntax> attributeLists,
        string owner,
        List<SecuritySchemeDeclaration> schemes,
        List<string> grants,
        List<string> misplaced,
        CancellationToken cancellationToken) {
        foreach (var attributeList in attributeLists) {
            foreach (var attribute in attributeList.Attributes) {
                cancellationToken.ThrowIfCancellationRequested();

                if (context.SemanticModel.GetTypeInfo(attribute, cancellationToken).Type
                    is not INamedTypeSymbol type) {
                    continue;
                }

                var definition = type.OriginalDefinition.ToDisplayString();

                if (definition.StartsWith(AuthorizeAttributeName + "<", System.StringComparison.Ordinal) &&
                    type.TypeArguments.Length >= 1 &&
                    type.TypeArguments[0] is INamedTypeSymbol schemeType &&
                    ImplementsScheme(schemeType)) {
                    var declaration = Declare(schemeType);

                    if (declaration != null &&
                        !schemes.Exists(existing => existing.Name == declaration.Name)) {
                        schemes.Add(declaration);
                    }
                } else if (definition == GrantsAttributeName) {
                    // The literal form only. The generic form computes its grants in a provider
                    // the generator cannot run.
                    LiteralGrants(attribute, context, grants, cancellationToken);
                } else if (type.Name is "HttpAuthenticationSchemeAttribute"
                           or "ApiKeyAuthenticationSchemeAttribute"
                           or "OAuth2AuthenticationSchemeAttribute") {
                    // A scheme-shape attribute in a position nothing reads. It belongs on a scheme
                    // type named by [Authorize<TScheme>]; here it publishes nothing and enforces
                    // nothing, which is the silent no-op the second trial walked into.
                    var entry = owner + "|" + type.Name;

                    if (!misplaced.Contains(entry)) {
                        misplaced.Add(entry);
                    }
                }
            }
        }
    }

    private static bool ImplementsScheme(INamedTypeSymbol type) {
        foreach (var contract in type.AllInterfaces) {
            if (contract.ToDisplayString() == SchemeInterface) {
                return true;
            }
        }

        return false;
    }

    /// <summary>The literal arguments of <c>[AuthorizeGrants("a", "b")]</c>, from the syntax.</summary>
    private static void LiteralGrants(
        AttributeSyntax attribute, GeneratorSyntaxContext context, List<string> grants,
        CancellationToken cancellationToken) {
        if (attribute.ArgumentList == null) {
            return;
        }

        foreach (var argument in attribute.ArgumentList.Arguments) {
            var value = context.SemanticModel.GetConstantValue(argument.Expression, cancellationToken);

            if (value is { HasValue: true, Value: string grant } &&
                grant.Length > 0 && !grants.Contains(grant)) {
                grants.Add(grant);
            }
        }
    }

    /// <summary>
    /// The scheme type's declared shape as its <c>securitySchemes</c> entry, or null when the
    /// type carries no shape attribute - a scheme with no declarable shape is enforced and not
    /// published.
    /// </summary>
    private static SecuritySchemeDeclaration? Declare(INamedTypeSymbol schemeType) {
        foreach (var attribute in schemeType.GetAttributes()) {
            switch (attribute.AttributeClass?.Name) {
                case "HttpAuthenticationSchemeAttribute": {
                    var scheme = Argument(attribute, 0) ?? "bearer";
                    var json = "{\"type\":\"http\",\"scheme\":\"" + Escape(scheme) + "\"";

                    if (Named(attribute, "BearerFormat") is { } format) {
                        json += ",\"bearerFormat\":\"" + Escape(format) + "\"";
                    }

                    json += Description(attribute) + "}";

                    return new SecuritySchemeDeclaration(schemeType.Name, json, carriesScopes: false);
                }

                case "ApiKeyAuthenticationSchemeAttribute": {
                    var name = Argument(attribute, 0) ?? "";
                    var location = attribute.ConstructorArguments.Length > 1
                        ? attribute.ConstructorArguments[1].Value switch {
                            1 => "query",
                            2 => "cookie",
                            _ => "header"
                        }
                        : "header";

                    var json = "{\"type\":\"apiKey\",\"name\":\"" + Escape(name) +
                               "\",\"in\":\"" + location + "\"" + Description(attribute) + "}";

                    return new SecuritySchemeDeclaration(schemeType.Name, json, carriesScopes: false);
                }

                case "OAuth2AuthenticationSchemeAttribute": {
                    var flow = attribute.ConstructorArguments.Length > 0
                        ? attribute.ConstructorArguments[0].Value switch {
                            1 => "clientCredentials",
                            2 => "implicit",
                            3 => "password",
                            _ => "authorizationCode"
                        }
                        : "authorizationCode";

                    var json = "{\"type\":\"oauth2\",\"flows\":{\"" + flow + "\":{";
                    var first = true;

                    if (Named(attribute, "AuthorizationUrl") is { } authorization) {
                        json += "\"authorizationUrl\":\"" + Escape(authorization) + "\"";
                        first = false;
                    }

                    if (Named(attribute, "TokenUrl") is { } token) {
                        json += (first ? "" : ",") + "\"tokenUrl\":\"" + Escape(token) + "\"";
                        first = false;
                    }

                    if (Named(attribute, "RefreshUrl") is { } refresh) {
                        json += (first ? "" : ",") + "\"refreshUrl\":\"" + Escape(refresh) + "\"";
                        first = false;
                    }

                    json += (first ? "" : ",") + "\"scopes\":{}}}" + Description(attribute) + "}";

                    return new SecuritySchemeDeclaration(schemeType.Name, json, carriesScopes: true);
                }
            }
        }

        return null;
    }

    private static string RequirementJson(
        SecuritySchemeDeclaration scheme, IReadOnlyList<string> grants) {
        if (!scheme.CarriesScopes || grants.Count == 0) {
            return "{\"" + Escape(scheme.Name) + "\":[]}";
        }

        var builder = new System.Text.StringBuilder("{\"")
            .Append(Escape(scheme.Name)).Append("\":[");

        for (var i = 0; i < grants.Count; i++) {
            if (i > 0) {
                builder.Append(',');
            }

            builder.Append('"').Append(Escape(grants[i])).Append('"');
        }

        return builder.Append("]}").ToString();
    }

    private static string? Argument(AttributeData attribute, int index) =>
        attribute.ConstructorArguments.Length > index
            ? attribute.ConstructorArguments[index].Value as string
            : null;

    private static string? Named(AttributeData attribute, string name) {
        foreach (var argument in attribute.NamedArguments) {
            if (argument.Key == name && argument.Value.Value is string value && value.Length > 0) {
                return value;
            }
        }

        return null;
    }

    private static string Description(AttributeData attribute) =>
        Named(attribute, "Description") is { } description
            ? ",\"description\":\"" + Escape(description) + "\""
            : "";

    private static string Escape(string value) =>
        OpenApiDocument.JsonSchemaWriter.Escape(value);
}
