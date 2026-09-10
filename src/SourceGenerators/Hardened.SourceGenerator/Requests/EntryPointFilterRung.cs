using System.Collections.Generic;
using Hardened.SourceGenerator.Requests;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.Shared;

/// <summary>
/// The filters an entry point declares for every handler in its compilation.
/// </summary>
/// <remarks>
/// <para>
/// A filter attribute on a handler method or on its controller is read by
/// <c>BaseRequestModelGenerator.GetFilters</c> into that handler's metadata array. One covering the
/// whole application has no handler to sit on, and the route that leaves the compilation -
/// <c>[Enable&lt;T&gt;]</c> and a global registration - is invisible to the generator that writes
/// the document. Read here instead, while the entry point's symbols are in reach, so both halves
/// see the same declaration.
/// </para>
/// <para>
/// <b>Whatever implements <c>IRequestFilterProvider</c>, and nothing else.</b> The handler rungs
/// take a denylist - anything that is not a route or a response declaration is a filter - which is
/// safe on a handler and not on an entry point, where <c>[HardenedModule]</c>, a runtime marker and
/// every <c>[Enable&lt;T&gt;]</c> sit in the same list. It also keeps the two halves reading one
/// set: the document publishes what the pipeline installs, rather than a status from an attribute
/// that contributes no filter.
/// </para>
/// <para>
/// The facets are read from the same declarations for that reason, through
/// <see cref="FilterResponseSelector.ReadDeclarations"/> and unnarrowed. Which operations one
/// reaches is decided per handler, where the verb and the response shape are known.
/// </para>
/// </remarks>
public static partial class EntryPointSelector {

    private const string FilterProvider = "IRequestFilterProvider";

    private const string FilterProviderNamespace = "Hardened.Requests.Abstract.RequestFilter";

    static partial void ReadFilterRung(
        GeneratorSyntaxContext context,
        ClassDeclarationSyntax entryPoint,
        Model model,
        CancellationToken cancellationToken) {
        List<AttributeSyntax>? declarations = null;

        foreach (var list in entryPoint.AttributeLists) {
            foreach (var attribute in list.Attributes) {
                cancellationToken.ThrowIfCancellationRequested();

                if (IsFilterProvider(context, attribute)) {
                    (declarations ??= new List<AttributeSyntax>()).Add(attribute);
                }
            }
        }

        if (declarations == null) {
            return;
        }

        var models = new List<AttributeModel>(declarations.Count);

        foreach (var declaration in declarations) {
            if (AttributeModelHelper.GetAttribute(context, declaration) is { } attributeModel) {
                models.Add(attributeModel);
            }
        }

        model.FilterDeclarations = models;
        model.FilterFacts =
            FilterResponseSelector.ReadDeclarations(context, declarations, cancellationToken);
    }

    /// <summary>
    /// Whether this attribute's own type provides a filter.
    /// </summary>
    /// <remarks>
    /// By interface rather than by name, so an application's own filter attribute is declarable at
    /// the entry point on the same terms as one this framework ships. Matched on the interface's
    /// namespace as well as its name, because a name alone is something any assembly can spell.
    /// </remarks>
    private static bool IsFilterProvider(GeneratorSyntaxContext context, AttributeSyntax attribute) {
        if (context.SemanticModel.GetSymbolInfo(attribute).Symbol?.ContainingType is not { } type) {
            return false;
        }

        foreach (var contract in type.AllInterfaces) {
            if (contract.Name == FilterProvider &&
                contract.ContainingNamespace?.ToDisplayString() == FilterProviderNamespace) {
                return true;
            }
        }

        return false;
    }
}
