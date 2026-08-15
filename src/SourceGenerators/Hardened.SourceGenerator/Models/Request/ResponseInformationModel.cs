using CSharpAuthor;

namespace Hardened.SourceGenerator.Models.Request;

public record ResponseInformationModel {
    public bool IsAsync { get; set; }

    public bool IsAsyncEnumerable { get; set; }

    public ITypeDefinition? AsyncEnumerableItemType { get; set; }

    public ITypeDefinition? ReturnType { get; set; }

    /// <summary>
    /// The view named by <c>[Template&lt;T&gt;]</c>, or null.
    /// </summary>
    /// <remarks>
    /// A type rather than a name, and that is the whole of the template design: the attribute is
    /// applied in the application's own assembly, so RazorBlade's <c>internal</c> generated classes
    /// are nameable there, and the compiler enforces both the interface and the parameterless
    /// constructor at the attribute.
    /// </remarks>
    public ITypeDefinition? TemplateType { get; set; }

    public int? DefaultStatusCode { get; set; }

    public string? RawResponseContentType { get; set; }

    /// <summary>
    /// Both ways a handler can say something about its response, not one of them.
    /// </summary>
    /// <remarks>
    /// This is what a caching failure is read through, and either property changing alone has to be
    /// visible in it. It has reported one and dropped the other twice now, in each direction, both
    /// times as a side effect of adding or removing the template annotation.
    /// </remarks>
    public override string ToString() {
        return $"{IsAsync}:{TemplateType}:{RawResponseContentType}:{ReturnType}";
    }
}