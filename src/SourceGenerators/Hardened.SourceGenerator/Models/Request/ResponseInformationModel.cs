using CSharpAuthor;

namespace Hardened.SourceGenerator.Models.Request;

public record ResponseInformationModel {
    public bool IsAsync { get; set; }

    public bool IsAsyncEnumerable { get; set; }

    public ITypeDefinition? AsyncEnumerableItemType { get; set; }

    public ITypeDefinition? ReturnType { get; set; }

    public string? TemplateName { get; set; }

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
        return $"{IsAsync}:{TemplateName}:{RawResponseContentType}:{ReturnType}";
    }
}