using System.Collections.Generic;
using System.Linq;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.OpenApiDocument;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.Requests;

/// <summary>
/// The responses a handler's declarations can be answered with that its return type says nothing
/// about: a 403 from an authorization attribute, a 429 from a rate limit, a 504 from a deadline.
/// </summary>
/// <remarks>
/// <para>
/// These join the ones a <c>Response</c> or union return type declares and the ones
/// <c>[Throws&lt;T&gt;]</c> declares, in the same <c>ResponseSchemas</c> list and through the same
/// document writer, so a status reaches the document by one path however it was declared. Like
/// <c>[Throws&lt;T&gt;]</c> they name only failures, so the success still comes from the return
/// type.
/// </para>
/// <para>
/// <b>Nothing here knows what a filter does.</b> The declaration carries <c>[AnswersStatus]</c> and
/// this reads it, on the attribute's own type, on a base type, or on an interface it implements -
/// which is how <c>IAuthorizeAttribute</c> publishes the 403 for every authorization attribute at
/// once, an application's own included. A filter vocabulary this framework never sees is published
/// the same way and needs no change here.
/// </para>
/// <para>
/// <b>The method, its class, then the assembly</b>, because all three guard the operation. A
/// controller carrying <c>[AuthorizeGrants]</c> guards every method on it, and a deadline written
/// once beside a library bounds every handler in it; a document that published a refusal only where
/// the attribute was repeated would describe the rest as unable to refuse.
/// </para>
/// </remarks>
public static class FilterResponseSelector {
    private const string AnswersStatus = "AnswersStatusAttribute";

    private const string AnswersStatusNamespace = "Hardened.Requests.Abstract.Responses";

    /// <summary>
    /// Every status the declarations covering <paramref name="method"/> can answer, deduplicated
    /// and in status order.
    /// </summary>
    /// <remarks>
    /// Deduplicated because the three levels overlap and because two attributes may answer the same
    /// status - two rate limits on one operation are one 429. The nearest declaration wins, which
    /// is how the runtime resolves everything else a handler declares twice.
    /// </remarks>
    public static IReadOnlyList<ResponseSchemaModel> Read(
        GeneratorSyntaxContext context,
        MethodDeclarationSyntax method,
        CancellationToken cancellationToken) {
        Dictionary<int, ResponseSchemaModel>? byStatus = null;

        // What has already spoken, keyed on the facet rather than on the status it resolved to.
        // A [Timeout(Status = 503)] on the method and a plain [Timeout] on the assembly are one
        // declaration answered two ways, so keying on the status would let the nearer one take 503
        // and the further one add a 504 the operation can never answer.
        HashSet<string>? spoken = null;

        foreach (var declaration in Declarations(context, method)) {
            cancellationToken.ThrowIfCancellationRequested();

            if (declaration.Type == null) {
                continue;
            }

            foreach (var (carrier, facet) in Facets(declaration.Type)) {
                if (!(spoken ??= new HashSet<string>()).Add(Key(carrier, facet))) {
                    continue;
                }

                var status = StatusFor(declaration, facet);

                if (status == null || (byStatus?.ContainsKey(status.Value) ?? false)) {
                    continue;
                }

                var body = facet.ConstructorArguments.Length > 1
                    ? facet.ConstructorArguments[1].Value as INamedTypeSymbol
                    : null;

                (byStatus ??= new Dictionary<int, ResponseSchemaModel>()).Add(
                    status.Value,
                    new ResponseSchemaModel(
                        status.Value,
                        Named(facet, "Description") as string ??
                        HttpResponseDescription.For(status.Value),
                        body == null
                            ? null
                            : JsonSchemaWriter.Write(
                                body, context.SemanticModel.Compilation.Assembly)));
            }
        }

        return byStatus == null
            ? System.Array.Empty<ResponseSchemaModel>()
            : byStatus.OrderBy(entry => entry.Key).Select(entry => entry.Value).ToList();
    }

    /// <summary>
    /// One declaration written on a handler, its class or its assembly: the attribute's type, and
    /// how to read a value it was written with.
    /// </summary>
    /// <remarks>
    /// Two ways in, because the two sources are reachable differently. A method's or a class's
    /// attribute is syntax this semantic model covers, so its arguments are read through the model;
    /// an assembly's lives in whichever file its author put it in, and is only reachable as a
    /// symbol - where the arguments are already bound and need no model at all.
    /// </remarks>
    private readonly struct Declaration {
        private readonly GeneratorSyntaxContext _context;
        private readonly AttributeSyntax? _syntax;
        private readonly AttributeData? _data;

        private Declaration(
            GeneratorSyntaxContext context,
            INamedTypeSymbol? type,
            AttributeSyntax? syntax,
            AttributeData? data) {
            _context = context;
            _syntax = syntax;
            _data = data;
            Type = type;
        }

        public INamedTypeSymbol? Type { get; }

        public static Declaration FromSyntax(GeneratorSyntaxContext context, AttributeSyntax syntax) =>
            new(context,
                context.SemanticModel.GetSymbolInfo(syntax).Symbol?.ContainingType,
                syntax,
                data: null);

        public static Declaration FromSymbol(GeneratorSyntaxContext context, AttributeData data) =>
            new(context, data.AttributeClass, syntax: null, data);

        /// <summary>The value this declaration gave <paramref name="property"/>, or null.</summary>
        public int? Written(string property) {
            if (_data != null) {
                foreach (var argument in _data.NamedArguments) {
                    if (argument.Key == property && argument.Value.Value is int bound) {
                        return bound;
                    }
                }

                return null;
            }

            var written = _syntax?.ArgumentList?.Arguments.FirstOrDefault(
                candidate => candidate.NameEquals?.Name.Identifier.Text == property);

            if (written == null) {
                return null;
            }

            return _context.SemanticModel.GetConstantValue(written.Expression) is
                { HasValue: true, Value: int status }
                ? status
                : null;
        }
    }

    /// <summary>
    /// The declarations covering this handler, nearest first: the method's, then its class's, then
    /// the assembly's.
    /// </summary>
    private static IEnumerable<Declaration> Declarations(
        GeneratorSyntaxContext context, MethodDeclarationSyntax method) {
        foreach (var list in method.AttributeLists) {
            foreach (var attribute in list.Attributes) {
                yield return Declaration.FromSyntax(context, attribute);
            }
        }

        if (method.Parent is TypeDeclarationSyntax declaringType) {
            foreach (var list in declaringType.AttributeLists) {
                foreach (var attribute in list.Attributes) {
                    yield return Declaration.FromSyntax(context, attribute);
                }
            }
        }

        // The compilation's own assembly, which is the handler's, which is the one the runtime
        // resolves that rung against. A referenced library's declaration bounds that library's
        // handlers and belongs in that library's document.
        foreach (var attribute in context.SemanticModel.Compilation.Assembly.GetAttributes()) {
            yield return Declaration.FromSymbol(context, attribute);
        }
    }

    /// <summary>
    /// Every <c>[AnswersStatus]</c> reachable from the declaration: its own, its base types', and
    /// the ones on any interface it implements.
    /// </summary>
    /// <remarks>
    /// Walked rather than read straight off the symbol, because Roslyn reports neither an inherited
    /// attribute nor one written on an interface. Both are the cases that matter here: an
    /// authorization attribute states nothing itself and gets its 403 from
    /// <c>IAuthorizeAttribute</c>.
    /// </remarks>
    private static IEnumerable<(INamedTypeSymbol Carrier, AttributeData Facet)> Facets(
        INamedTypeSymbol declaration) {
        for (var type = declaration; type != null; type = type.BaseType) {
            foreach (var attribute in type.GetAttributes()) {
                if (IsAnswersStatus(attribute)) {
                    yield return (type, attribute);
                }
            }
        }

        foreach (var contract in declaration.AllInterfaces) {
            foreach (var attribute in contract.GetAttributes()) {
                if (IsAnswersStatus(attribute)) {
                    yield return (contract, attribute);
                }
            }
        }
    }

    /// <summary>
    /// What identifies a facet across the levels: the type that carries it and which of its
    /// statuses this is.
    /// </summary>
    /// <remarks>
    /// The carrier rather than the attribute written, so <c>[AuthorizeGrants]</c> on a method and
    /// <c>[RequireAuthorization]</c> on its class are one 403 - they reach the same declaration on
    /// <c>IAuthorizeAttribute</c>. The status distinguishes two facets on one carrier.
    /// </remarks>
    private static string Key(INamedTypeSymbol carrier, AttributeData facet) =>
        carrier.ToDisplayString() + "#" +
        (facet.ConstructorArguments.Length > 0 ? facet.ConstructorArguments[0].Value : null);

    /// <summary>
    /// By namespace as well as name, so an application's own <c>AnswersStatus</c> is not mistaken
    /// for a statement about this framework's document.
    /// </summary>
    private static bool IsAnswersStatus(AttributeData attribute) =>
        attribute.AttributeClass?.Name == AnswersStatus &&
        attribute.AttributeClass.ContainingNamespace?.ToDisplayString() == AnswersStatusNamespace;

    /// <summary>
    /// The status the facet declares, or the one the declaration was written with where the facet
    /// names a property that overrides it.
    /// </summary>
    private static int? StatusFor(Declaration declaration, AttributeData facet) {
        if (Named(facet, "StatusFrom") is string property &&
            declaration.Written(property) is { } written) {
            return written;
        }

        return facet.ConstructorArguments.Length > 0 &&
               facet.ConstructorArguments[0].Value is int status
            ? status
            : null;
    }

    private static object? Named(AttributeData facet, string name) {
        foreach (var argument in facet.NamedArguments) {
            if (argument.Key == name) {
                return argument.Value.Value;
            }
        }

        return null;
    }
}
