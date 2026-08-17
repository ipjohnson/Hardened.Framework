using CSharpAuthor;

namespace Hardened.SourceGenerator.Models.Request;

public record ResponseInformationModel {
    public bool IsAsync { get; set; }

    public bool IsAsyncEnumerable { get; set; }

    public ITypeDefinition? AsyncEnumerableItemType { get; set; }

    public ITypeDefinition? ReturnType { get; set; }

    /// <summary>
    /// What writes this response, named by <c>[Output&lt;T&gt;]</c>, or null.
    /// </summary>
    /// <remarks>
    /// A type rather than a name, and that is the whole of the design: the attribute is applied in
    /// the application's own assembly, so RazorBlade's <c>internal</c> generated classes are
    /// nameable there, and the compiler enforces both the interface and the parameterless
    /// constructor at the attribute.
    /// </remarks>
    public ITypeDefinition? OutputType { get; set; }

    /// <summary>
    /// The media type an OpenAPI document declared for the success response, when it named one
    /// that is not JSON.
    /// </summary>
    /// <remarks>
    /// Not <see cref="RawResponseContentType"/>, which commits the response to a content type and
    /// takes it out of negotiation. This only records what the contract says, so a document
    /// promising rendered HTML for a model can be checked against an implementation that names no
    /// view to render it.
    /// </remarks>
    public string? DeclaredContentType { get; set; }

    /// <summary>
    /// Whether the success response is an object or a list of them, rather than a scalar.
    /// </summary>
    /// <remarks>
    /// The half of <see cref="DeclaredContentType"/> that makes it actionable. A handler returning
    /// a string can answer <c>text/html</c> by writing it; a handler returning a model cannot,
    /// because there is nothing that serializes an object as HTML without a view.
    /// </remarks>
    public bool RendersAModel { get; set; }

    public int? DefaultStatusCode { get; set; }

    public string? RawResponseContentType { get; set; }

    /// <summary>
    /// How a streamed response is framed on the wire, or null for newline-delimited JSON.
    /// </summary>
    /// <remarks>
    /// Named rather than typed, because the generator emits a reference to a runtime type it does
    /// not link. Only meaningful when <see cref="IsAsyncEnumerable"/> is true; the generator
    /// reports it as a build error otherwise, since there is no stream to frame.
    /// </remarks>
    public string? StreamFraming { get; set; }

    /// <summary>
    /// Both ways a handler can say something about its response, not one of them.
    /// </summary>
    /// <remarks>
    /// This is what a caching failure is read through, and either property changing alone has to be
    /// visible in it. It has reported one and dropped the other twice now, in each direction, both
    /// times as a side effect of adding or removing the template annotation.
    /// </remarks>
    public override string ToString() {
        return $"{IsAsync}:{OutputType}:{RawResponseContentType}:{StreamFraming}:{ReturnType}";
    }
}