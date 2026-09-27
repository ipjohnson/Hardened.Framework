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
/// <b>Whatever implements <c>IRequestFilterProvider</c> or <c>IAuthorizeAttribute</c>, and nothing
/// else.</b> The handler rungs take a denylist - anything that is not a route or a response
/// declaration is a filter - which is safe on a handler and not on an entry point, where
/// <c>[HardenedModule]</c>, a runtime marker and every <c>[Enable&lt;T&gt;]</c> sit in the same
/// list. It also keeps the two halves reading one set: the document publishes what the pipeline
/// installs, rather than a status from an attribute that contributes no filter.
/// </para>
/// <para>
/// A requirement contributes no filter of its own. The pipeline conjoins it into the requirement of
/// every handler it reaches, and <c>[AllowAnonymous]</c> on a handler cancels it there as it cancels
/// one written on the handler's class. Left out of this reading, <c>[Authorize&lt;TScheme&gt;]</c>
/// on a module class compiles and guards nothing, which is where a rule meant for every route is
/// written.
/// </para>
/// <para>
/// The facets are read from the same declarations for that reason, through
/// <see cref="FilterResponseSelector.ReadDeclarations"/> and unnarrowed. Which operations one
/// reaches is decided per handler, where the verb and the response shape are known. A
/// requirement's facets are read apart from the filters', into <see cref="EntryPointSecurity"/>,
/// because which operations they reach also depends on <c>[AllowAnonymous]</c>.
/// </para>
/// </remarks>
public static partial class EntryPointSelector
{
    private const string FilterProvider = "IRequestFilterProvider";

    private const string FilterProviderNamespace = "Hardened.Requests.Abstract.RequestFilter";

    private const string Requirement = "IAuthorizeAttribute";

    private const string RequirementNamespace = "Hardened.Requests.Abstract.Authorization";

    static partial void ReadFilterRung(
        GeneratorSyntaxContext context,
        ClassDeclarationSyntax entryPoint,
        Model model,
        CancellationToken cancellationToken
    )
    {
        List<AttributeSyntax>? declarations = null;
        List<AttributeSyntax>? filters = null;
        List<AttributeSyntax>? requirements = null;

        foreach (var list in entryPoint.AttributeLists)
        {
            foreach (var attribute in list.Attributes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (Implements(context, attribute, Requirement, RequirementNamespace))
                {
                    (requirements ??= new List<AttributeSyntax>()).Add(attribute);
                }
                else if (Implements(context, attribute, FilterProvider, FilterProviderNamespace))
                {
                    (filters ??= new List<AttributeSyntax>()).Add(attribute);
                }
                else
                {
                    continue;
                }

                (declarations ??= new List<AttributeSyntax>()).Add(attribute);
            }
        }

        if (declarations == null)
        {
            return;
        }

        var models = new List<AttributeModel>(declarations.Count);

        foreach (var declaration in declarations)
        {
            if (AttributeModelHelper.GetAttribute(context, declaration) is { } attributeModel)
            {
                models.Add(attributeModel);
            }
        }

        model.FilterDeclarations = models;

        if (filters != null)
        {
            model.FilterFacts = FilterResponseSelector.ReadDeclarations(
                context,
                filters,
                cancellationToken
            );
        }

        if (requirements != null)
        {
            model.DeclaresRequirement = true;
            model.SecurityFacts = EntryPointSecurity.Read(context, requirements, cancellationToken);
        }
    }

    /// <summary>
    /// Whether this attribute's own type implements the named interface.
    /// </summary>
    /// <remarks>
    /// By interface rather than by name, so an application's own filter or requirement attribute is
    /// declarable at the entry point on the same terms as one this framework ships. Matched on the
    /// interface's namespace as well as its name, because a name alone is something any assembly
    /// can spell.
    /// </remarks>
    private static bool Implements(
        GeneratorSyntaxContext context,
        AttributeSyntax attribute,
        string name,
        string containingNamespace
    )
    {
        if (context.SemanticModel.GetSymbolInfo(attribute).Symbol?.ContainingType is not { } type)
        {
            return false;
        }

        foreach (var contract in type.AllInterfaces)
        {
            if (
                contract.Name == name
                && contract.ContainingNamespace?.ToDisplayString() == containingNamespace
            )
            {
                return true;
            }
        }

        return false;
    }
}
