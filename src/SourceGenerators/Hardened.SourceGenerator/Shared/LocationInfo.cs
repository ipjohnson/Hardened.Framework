using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Hardened.SourceGenerator.Shared;

/// <summary>
/// Where something was written, as plain data a model can carry.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="Location"/> holds the <see cref="SyntaxTree"/> it came from, so putting one in a
/// model handed to an incremental provider keeps that tree alive for as long as the model is cached
/// and makes the model unequal to its predecessor whenever the file is reparsed. This carries the
/// same information as three values that compare by content.
/// </para>
/// <para>
/// <b>It still churns, and that is why what carries it matters.</b> A span is an offset, so an edit
/// anywhere above a declaration shifts it and the model changes even though nothing about the
/// declaration did. A model that also feeds code generation therefore pays for every keystroke above
/// it - measured at 21 of 23 outputs recomputed for a comment above twenty handlers. Carried by a
/// model that feeds nothing but diagnostics, the same churn costs a diagnostic scan and no emitted
/// source at all.
/// </para>
/// </remarks>
public record LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan) {

    /// <summary>Captures where a node was written.</summary>
    public static LocationInfo? From(SyntaxNode? node) => From(node?.GetLocation());

    /// <summary>Captures where a token was written - an identifier, rather than its declaration.</summary>
    public static LocationInfo? From(SyntaxToken token) => From(token.GetLocation());

    private static LocationInfo? From(Location? location) {
        if (location == null || location.SourceTree == null) {
            return null;
        }

        return new LocationInfo(
            location.SourceTree.FilePath,
            location.SourceSpan,
            location.GetLineSpan().Span);
    }

    /// <summary>
    /// Rebuilds a reportable location, without the tree it came from.
    /// </summary>
    /// <remarks>
    /// <see cref="Location.Create(string, TextSpan, LinePositionSpan)"/> takes exactly what was
    /// captured, which is what makes the round trip lossless as far as a diagnostic is concerned:
    /// the IDE squiggles the right span and navigates to the right line.
    /// </remarks>
    public Location ToLocation() => Location.Create(FilePath, TextSpan, LineSpan);
}
