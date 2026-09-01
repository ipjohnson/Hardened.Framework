using System.Collections.Generic;
using System.Linq;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.OpenApiDocument;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.Requests;

/// <summary>
/// The non-2xx responses a handler declares with <c>[Throws&lt;T&gt;]</c>.
/// </summary>
/// <remarks>
/// <para>
/// These join the ones a <c>Response</c> or union return type declares, in the same
/// <c>ResponseSchemas</c> list and through the same document writer, so a status reaches the
/// document by one path however it was declared. Grouping and <c>oneOf</c> for two shapes under one
/// status come free from that.
/// </para>
/// <para>
/// The status is read off <c>[HttpStatus]</c> on the declared type, which is what a union case
/// reads too. A type carrying none must state its status in the attribute, and a declaration doing
/// neither is reported rather than dropped: a response the author wrote down and the document does
/// not carry is worse than one they never wrote.
/// </para>
/// </remarks>
public static class ThrownResponseSelector {
    private const string AttributeName = "Throws";
    private const string AttributeSuffix = "Attribute";
    private const string HttpStatusAttribute = "HttpStatusAttribute";

    /// <summary>The id reported when a declaration names no status.</summary>
    public const string DiagnosticId = "HRDT001";

    /// <summary>
    /// A <c>[Throws&lt;T&gt;]</c> declaration naming a type with no status and stating none.
    /// </summary>
    /// <remarks>
    /// Built per call rather than held in a static field: RS2008 looks for the field, and these
    /// projects set EnforceExtendedAnalyzerRules. The same arrangement the routing diagnostics use.
    /// </remarks>
    private static DiagnosticDescriptor Descriptor() => new(
        id: DiagnosticId,
        title: "The thrown type declares no status",
        messageFormat:
        "'[Throws<{0}>]' on '{1}' names a type carrying no [HttpStatus], and states no status of " +
        "its own. Write '[Throws<{0}>(409)]' with the status it answers, or put [HttpStatus] on " +
        "{0} - which is also what lets it be used as a case in a Response<> or union return type.",
        category: "Hardened.Responses",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);


    /// <summary>Reports the declarations a handler could not resolve a status for.</summary>
    public static void Report(
        SourceProductionContext context, string handler, string? unresolved) {
        if (string.IsNullOrEmpty(unresolved)) {
            return;
        }

        foreach (var name in unresolved!.Split(',')) {
            context.ReportDiagnostic(Diagnostic.Create(Descriptor(), Location.None, name, handler));
        }
    }

    /// <summary>
    /// Reads every <c>[Throws&lt;T&gt;]</c> on the method, collecting into <paramref name="unresolved"/>
    /// the ones naming no status.
    /// </summary>
    public static IReadOnlyList<ResponseSchemaModel> Read(
        GeneratorSyntaxContext context,
        MethodDeclarationSyntax method,
        List<string> unresolved,
        CancellationToken cancellationToken) {
        List<ResponseSchemaModel>? declared = null;

        foreach (var attributeList in method.AttributeLists) {
            foreach (var attribute in attributeList.Attributes) {
                cancellationToken.ThrowIfCancellationRequested();

                var errorType = ThrownType(context, attribute);

                if (errorType == null) {
                    continue;
                }

                var status = StatedStatus(context, attribute) ?? DeclaredStatus(errorType);

                if (status == null) {
                    unresolved.Add(errorType.Name);

                    continue;
                }

                declared ??= new List<ResponseSchemaModel>();

                declared.Add(new ResponseSchemaModel(
                    status.Value,
                    Description(context, attribute) ?? HttpResponseDescription.For(status.Value),
                    OpenApiDocument.JsonSchemaWriter.Write(
                        errorType, context.SemanticModel.Compilation.Assembly)) {
                    // The headers the thrown type declares, by the same convention a returned
                    // case's are read - the symbol is already in hand here.
                    Headers = UnionResponseSelector.DeclaredHeaders(errorType)
                });
            }
        }

        return (IReadOnlyList<ResponseSchemaModel>?)declared ?? System.Array.Empty<ResponseSchemaModel>();
    }

    /// <summary>The type argument of a <c>[Throws&lt;T&gt;]</c>, or null for any other attribute.</summary>
    private static INamedTypeSymbol? ThrownType(GeneratorSyntaxContext context, AttributeSyntax attribute) {
        var generic = attribute.Name as GenericNameSyntax ??
                      (attribute.Name as QualifiedNameSyntax)?.Right as GenericNameSyntax ??
                      (attribute.Name as AliasQualifiedNameSyntax)?.Name as GenericNameSyntax;

        if (generic == null || generic.TypeArgumentList.Arguments.Count != 1) {
            return null;
        }

        var name = generic.Identifier.Text;

        if (name != AttributeName && name != AttributeName + AttributeSuffix) {
            return null;
        }

        return context.SemanticModel
            .GetSymbolInfo(generic.TypeArgumentList.Arguments[0]).Symbol as INamedTypeSymbol;
    }

    /// <summary>The status given to the attribute, for a type that carries none.</summary>
    private static int? StatedStatus(GeneratorSyntaxContext context, AttributeSyntax attribute) {
        var argument = attribute.ArgumentList?.Arguments
            .FirstOrDefault(candidate => candidate.NameEquals == null);

        if (argument == null) {
            return null;
        }

        return context.SemanticModel.GetConstantValue(argument.Expression) is { HasValue: true, Value: int status }
            ? status
            : null;
    }

    /// <summary>The status <c>[HttpStatus]</c> puts on the type itself.</summary>
    private static int? DeclaredStatus(INamedTypeSymbol errorType) {
        foreach (var attribute in errorType.GetAttributes()) {
            if (attribute.AttributeClass?.Name != HttpStatusAttribute) {
                continue;
            }

            if (attribute.ConstructorArguments.Length == 1 &&
                attribute.ConstructorArguments[0].Value is int status) {
                return status;
            }
        }

        return null;
    }

    private static string? Description(GeneratorSyntaxContext context, AttributeSyntax attribute) {
        var named = attribute.ArgumentList?.Arguments
            .FirstOrDefault(candidate => candidate.NameEquals?.Name.Identifier.Text == "Description");

        return named != null &&
               context.SemanticModel.GetConstantValue(named.Expression) is { HasValue: true, Value: string text }
            ? text
            : null;
    }
}
