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

    private const string AnswersHeader = "AnswersHeaderAttribute";

    private const string ReadsHeader = "ReadsHeaderAttribute";

    /// <summary>
    /// Where the two declaration attributes live. They stayed in Abstract through the HTTP
    /// extraction because <c>TimeoutAttribute</c> and <c>RateLimitAttribute</c> in
    /// <c>Hardened.Requests.Runtime</c> declare what they answer through them, and that runtime
    /// cannot reference a web package.
    /// </summary>
    private const string DeclarationNamespace = "Hardened.Requests.Abstract.Responses";

    /// <summary>
    /// Where <c>[ReadsHeader]</c> lives, which is not the same place. Reading a request header is
    /// an HTTP idea with no non-web caller, so it moved with the responses while its two siblings
    /// did not.
    /// </summary>
    private const string ReadsHeaderNamespace = "Hardened.Web.Runtime.Responses";

    /// <summary>
    /// Every status the declarations covering <paramref name="method"/> can answer, deduplicated
    /// and in status order, plus the headers those declarations say the operation writes and
    /// reads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deduplicated because the three levels overlap and because two attributes may answer the same
    /// status - two rate limits on one operation are one 429. The nearest declaration wins, which
    /// is how the runtime resolves everything else a handler declares twice.
    /// </para>
    /// <para>
    /// Unnarrowed. Which of these reach the operation depends on its verb and on whether it
    /// streams, and one of the two front ends reading this does not know either yet. See
    /// <see cref="DeclaredOperationFacts"/>.
    /// </para>
    /// </remarks>
    internal static DeclaredOperationFacts Read(
        GeneratorSyntaxContext context,
        MethodDeclarationSyntax method,
        CancellationToken cancellationToken) =>
        Collect(
            context,
            Declarations(context, method),
            Written(context, method),
            cancellationToken);

    /// <summary>
    /// The same reading, over declarations that are not on a handler at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The entry point's rung. A filter declared there covers every handler in the compilation, so
    /// what it answers belongs on every operation the same declaration installs on - narrowed by
    /// the same <see cref="DeclaredScope"/>, since a declaration that reaches only the reads
    /// publishes only on the reads.
    /// </para>
    /// <para>
    /// No <see cref="Written"/> rung. That one reads <c>[AnswersHeader]</c> and
    /// <c>[ReadsHeader]</c> written directly beside a handler, where the operation is the subject.
    /// An entry point is not an operation, and a header declared there would describe every route
    /// in the application without a filter behind it to write one.
    /// </para>
    /// </remarks>
    internal static DeclaredOperationFacts ReadDeclarations(
        GeneratorSyntaxContext context,
        IEnumerable<AttributeSyntax> declarations,
        CancellationToken cancellationToken) =>
        Collect(
            context,
            declarations.Select(attribute => Declaration.FromSyntax(context, attribute)),
            Enumerable.Empty<(ISymbol, AttributeData)>(),
            cancellationToken);

    private static DeclaredOperationFacts Collect(
        GeneratorSyntaxContext context,
        IEnumerable<Declaration> declarations,
        IEnumerable<(ISymbol Carrier, AttributeData Facet)> written,
        CancellationToken cancellationToken) {
        Dictionary<int, ScopedRefusal>? byStatus = null;
        List<ScopedResponseHeader>? responseHeaders = null;
        List<ScopedRequestHeader>? requestHeaders = null;

        // What has already spoken, keyed on the facet rather than on the status it resolved to.
        // A [Timeout(Status = 503)] on the method and a plain [Timeout] on the assembly are one
        // declaration answered two ways, so keying on the status would let the nearer one take 503
        // and the further one add a 504 the operation can never answer.
        HashSet<string>? spoken = null;

        foreach (var declaration in declarations) {
            cancellationToken.ThrowIfCancellationRequested();

            if (declaration.Type == null) {
                continue;
            }

            foreach (var (carrier, facet) in Facets(declaration.Type, AnswersStatus)) {
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

                (byStatus ??= new Dictionary<int, ScopedRefusal>()).Add(
                    status.Value,
                    new ScopedRefusal(
                        new ResponseSchemaModel(
                            status.Value,
                            Named(facet, "Description") as string ??
                            HttpResponseDescription.For(status.Value),
                            body == null
                                ? null
                                : JsonSchemaWriter.Write(
                                    body, context.SemanticModel.Compilation.Assembly)),
                        Scope(facet)));
            }

            foreach (var (carrier, facet) in Facets(declaration.Type, AnswersHeader)) {
                AddResponseHeader(carrier, facet, ref spoken, ref responseHeaders);
            }

            foreach (var (carrier, facet) in Facets(declaration.Type, ReadsHeader)) {
                AddRequestHeader(carrier, facet, ref spoken, ref requestHeaders);
            }
        }

        // And the ones written on the handler itself. Both header declarations go on a method or
        // its class as well as on an attribute's type - a throws-mode 201 has no other way to say
        // it carries a Location - and those are read off the symbol rather than through
        // Declarations, because Declarations carries an attribute's type and this needs the
        // arguments the attribute was written with.
        foreach (var (carrier, facet) in written) {
            if (Is(facet, AnswersHeader)) {
                AddResponseHeader(carrier, facet, ref spoken, ref responseHeaders);
            }
            else if (Is(facet, ReadsHeader)) {
                AddRequestHeader(carrier, facet, ref spoken, ref requestHeaders);
            }
        }

        if (byStatus == null && responseHeaders == null && requestHeaders == null) {
            return DeclaredOperationFacts.Empty;
        }

        return new DeclaredOperationFacts(
            byStatus == null
                ? System.Array.Empty<ScopedRefusal>()
                : byStatus.OrderBy(entry => entry.Key).Select(entry => entry.Value).ToList(),
            (IReadOnlyList<ScopedResponseHeader>?)responseHeaders ??
            System.Array.Empty<ScopedResponseHeader>(),
            (IReadOnlyList<ScopedRequestHeader>?)requestHeaders ??
            System.Array.Empty<ScopedRequestHeader>());
    }

    private static void AddResponseHeader(
        ISymbol carrier, AttributeData facet, ref HashSet<string>? spoken,
        ref List<ScopedResponseHeader>? headers) {
        if (facet.ConstructorArguments.Length < 2 ||
            facet.ConstructorArguments[0].Value is not int status ||
            facet.ConstructorArguments[1].Value is not string name) {
            return;
        }

        // Keyed on the name as well as the status, because two headers on one carrier at one
        // status are two headers - a 200 carrying both an ETag and a Last-Modified is the
        // ordinary conditional-request shape.
        if (!(spoken ??= new HashSet<string>()).Add(
                carrier.ToDisplayString() + "#h" + status + "#" + name)) {
            return;
        }

        (headers ??= new List<ScopedResponseHeader>()).Add(
            new ScopedResponseHeader(status, name, Named(facet, "Description") as string, Scope(facet)));
    }

    private static void AddRequestHeader(
        ISymbol carrier, AttributeData facet, ref HashSet<string>? spoken,
        ref List<ScopedRequestHeader>? headers) {
        if (facet.ConstructorArguments.Length < 1 ||
            facet.ConstructorArguments[0].Value is not string name) {
            return;
        }

        if (!(spoken ??= new HashSet<string>()).Add(carrier.ToDisplayString() + "#r#" + name)) {
            return;
        }

        (headers ??= new List<ScopedRequestHeader>()).Add(
            new ScopedRequestHeader(name, Named(facet, "Description") as string, Scope(facet)));
    }

    /// <summary>
    /// Every attribute written on the handler, its class and its assembly, as bound data.
    /// </summary>
    /// <remarks>
    /// The same three rungs <see cref="Declarations"/> walks and for the same reason, read off
    /// the symbols instead: an attribute written here is the declaration rather than a carrier of
    /// one, so what is wanted is its arguments and not its type.
    /// </remarks>
    private static IEnumerable<(ISymbol Carrier, AttributeData Facet)> Written(
        GeneratorSyntaxContext context, MethodDeclarationSyntax method) {
        if (context.SemanticModel.GetDeclaredSymbol(method) is not IMethodSymbol handler) {
            yield break;
        }

        foreach (var attribute in handler.GetAttributes()) {
            yield return (handler, attribute);
        }

        foreach (var attribute in handler.ContainingType.GetAttributes()) {
            yield return (handler.ContainingType, attribute);
        }

        foreach (var attribute in context.SemanticModel.Compilation.Assembly.GetAttributes()) {
            yield return (context.SemanticModel.Compilation.Assembly, attribute);
        }
    }

    /// <summary>The reach the declaration stated for itself.</summary>
    private static DeclaredScope Scope(AttributeData facet) =>
        new(Named(facet, "Methods") as string, Named(facet, "NotWhenStreaming") is true);

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
        INamedTypeSymbol declaration, string facetName) {
        for (var type = declaration; type != null; type = type.BaseType) {
            foreach (var attribute in type.GetAttributes()) {
                if (Is(attribute, facetName)) {
                    yield return (type, attribute);
                }
            }
        }

        foreach (var contract in declaration.AllInterfaces) {
            foreach (var attribute in contract.GetAttributes()) {
                if (Is(attribute, facetName)) {
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
    /// for a statement about this framework's document. The same holds for the two header
    /// declarations read through here.
    /// </summary>
    private static bool Is(AttributeData attribute, string facetName) =>
        attribute.AttributeClass != null && Is(attribute.AttributeClass, facetName);

    private static bool Is(INamedTypeSymbol type, string facetName) =>
        type.Name == facetName &&
        type.ContainingNamespace?.ToDisplayString() ==
        (facetName == ReadsHeader ? ReadsHeaderNamespace : DeclarationNamespace);

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
