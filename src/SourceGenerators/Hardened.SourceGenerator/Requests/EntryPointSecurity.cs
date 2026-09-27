using System.Collections.Generic;
using System.Linq;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.Requests;

/// <summary>
/// What the requirements an entry point declares publish on every operation they reach: the schemes
/// they name, the grants they name as literals, and the 403 their enforcement answers.
/// </summary>
/// <remarks>
/// <para>
/// Read the way a controller's <c>[Authorize&lt;TScheme&gt;]</c> and <c>[AuthorizeGrants]</c> are,
/// by <see cref="SecurityDeclarationSelector"/>, so a requirement written on the module class
/// publishes what the same requirement written on each controller would.
/// </para>
/// <para>
/// Value-equal, because it rides <c>EntryPointSelector.Model</c>, which keys the incremental cache.
/// </para>
/// </remarks>
internal sealed class EntryPointSecurity : IEntryPointFilterFacts, IEquatable<EntryPointSecurity>
{
    public EntryPointSecurity(
        IReadOnlyList<SecuritySchemeDeclaration> schemes,
        IReadOnlyList<string> grants,
        DeclaredOperationFacts refusals
    )
    {
        Schemes = schemes;
        Grants = grants;
        Refusals = refusals;
    }

    public IReadOnlyList<SecuritySchemeDeclaration> Schemes { get; }

    public IReadOnlyList<string> Grants { get; }

    /// <summary>
    /// The statuses the requirements answer, which is the 403 <c>IAuthorizeAttribute</c> declares.
    /// </summary>
    public DeclaredOperationFacts Refusals { get; }

    public bool IsEmpty => Schemes.Count == 0 && Grants.Count == 0 && Refusals.IsEmpty;

    public static EntryPointSecurity Read(
        GeneratorSyntaxContext context,
        IReadOnlyList<AttributeSyntax> requirements,
        CancellationToken cancellationToken
    )
    {
        var schemes = new List<SecuritySchemeDeclaration>();
        var grants = new List<string>();

        SecurityDeclarationSelector.ReadRequirements(
            context,
            requirements,
            schemes,
            grants,
            cancellationToken
        );

        return new EntryPointSecurity(
            schemes,
            grants,
            FilterResponseSelector.ReadDeclarations(context, requirements, cancellationToken)
        );
    }

    public bool Equals(EntryPointSecurity? other) =>
        other is not null
        && Schemes.SequenceEqual(other.Schemes)
        && Grants.SequenceEqual(other.Grants, StringComparer.Ordinal)
        && Refusals.Equals(other.Refusals);

    public override bool Equals(object? obj) => Equals(obj as EntryPointSecurity);

    public override int GetHashCode()
    {
        unchecked
        {
            return (Schemes.Count * 397) ^ Grants.Count ^ Refusals.GetHashCode();
        }
    }
}
