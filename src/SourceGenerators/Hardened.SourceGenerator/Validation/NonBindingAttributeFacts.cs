using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.Validation;

/// <summary>
/// Attributes on a handler parameter that say something other than where its value comes from.
/// </summary>
/// <remarks>
/// <para>
/// A handler's parameter attributes are open-ended: anything a generator does not recognise is
/// treated as a custom binder and emitted as one, and at run time
/// <c>ExecutionHelper.CustomAttributeData</c> throws for anything that is not an
/// <c>ICustomBindingAttribute</c>. So an unrecognised attribute does not merely fail to do its own
/// job - it takes the parameter out of the binding path it was written for and turns a valid
/// signature into a 500.
/// </para>
/// <para>
/// This gathers the exceptions in one place rather than leaving each generator to remember them.
/// It was two separate <c>continue</c>s on constraints before, one per generator, and
/// <c>[EnumeratorCancellation]</c> was missing from both.
/// </para>
/// <para>
/// Beside <see cref="ConstraintAttributeFacts"/> rather than under <c>Requests</c>, because
/// <c>Hardened.OpenApi.SourceGenerator</c> links <c>Requests/**</c> as source but not
/// <c>Validation/**</c> - so a file there referencing this one does not compile in that project.
/// </para>
/// </remarks>
public static class NonBindingAttributeFacts {

    private const string EnumeratorCancellation =
        "System.Runtime.CompilerServices.EnumeratorCancellationAttribute";

    /// <summary>
    /// Whether <paramref name="attribute"/> should be left alone by the binding path.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Constraints</b> describe the value, not its source. Without this
    /// <c>[StringLength(3)]</c> on a route parameter stops the parameter binding at all rather than
    /// merely failing to be validated.
    /// </para>
    /// <para>
    /// <b><c>[EnumeratorCancellation]</c></b> tells the C# compiler which parameter of an async
    /// iterator receives the token passed to <c>WithCancellation</c>. It is the spelling every
    /// author reaches for on <c>IAsyncEnumerable&lt;T&gt;</c>, and it is compiler machinery rather
    /// than a statement about binding - the parameter it sits on is a <c>CancellationToken</c>,
    /// which now binds by type.
    /// </para>
    /// <para>
    /// Resolved through the semantic model rather than by name, for the reason
    /// <see cref="ConstraintAttributeFacts"/> gives: a name is not proof, and treating it as proof
    /// takes someone's unrelated attribute out of the binding path it was written for.
    /// </para>
    /// </remarks>
    public static bool IsNonBinding(GeneratorSyntaxContext context, AttributeSyntax attribute) {
        if (ConstraintAttributeFacts.IsConstraint(context, attribute)) {
            return true;
        }

        var symbol = context.SemanticModel.GetSymbolInfo(attribute).Symbol;

        return symbol?.ContainingType?.ToDisplayString() == EnumeratorCancellation;
    }
}
