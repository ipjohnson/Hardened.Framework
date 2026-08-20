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

    /// <summary>
    /// The status a successful response carries, or null for 200.
    /// </summary>
    /// <remarks>
    /// Where the two front ends meet: a description's <c>responses:</c> key reaches this through
    /// <c>RequestModelBuilder</c>, and <c>[Post(SuccessStatus = 201)]</c> reaches it through
    /// <c>BaseRequestModelGenerator</c>. One field, so one runtime behaviour.
    /// </remarks>
    public int? DefaultStatusCode { get; set; }

    /// <summary>
    /// C# naming the instance a null return writes, or null to write nothing.
    /// </summary>
    /// <remarks>
    /// An expression rather than a value, because the instance is a generated <c>static readonly</c>
    /// field on the models - allocated once for the process and serialized by the generated
    /// <c>JsonTypeInfo</c> like any other response. It holds the status and its reason phrase and
    /// nothing about the request, which is the point of it.
    /// </remarks>
    public string? NullResponseBodyExpression { get; set; }

    /// <summary>
    /// Every media type this operation can produce, comma-separated, or null where it said nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Where the two front ends meet again: the <c>content:</c> keys of a described operation's
    /// success response, and <c>[SupportedContentTypes(...)]</c> on a hand-written one.
    /// </para>
    /// <para>
    /// A joined string rather than a list, because this is a <c>record</c> and a <c>List&lt;string&gt;</c>
    /// member would compare by reference - so two responses declaring the same types would look
    /// different to the incremental generator's cache, and two declaring different ones could look
    /// the same. <c>ToString</c> is what a caching failure is read through, and a value it cannot
    /// represent is a value that silently does not invalidate.
    /// </para>
    /// <para>
    /// Null means the operation said nothing, which is not the same as an empty set: nothing
    /// declared leaves negotiation exactly as it was.
    /// </para>
    /// </remarks>
    public string? ProducedContentTypes { get; set; }

    public string? RawResponseContentType { get; set; }

    /// <summary>
    /// The cases of a declared response set, encoded, or null where the handler returns one type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A joined string for the reason <see cref="ProducedContentTypes"/> is one, and the reasoning
    /// there applies here with more at stake: this is a <c>record</c>, so its synthesized equality
    /// is the cache key, and a <c>List&lt;UnionCaseModel&gt;</c> member would compare by reference.
    /// Two handlers declaring the same cases would look different to the incremental generator and
    /// two declaring different ones could look the same - and what a wrong answer produces here is
    /// a handler emitting the dispatch for a response set it no longer has.
    /// </para>
    /// <para>
    /// <c>UnionResponseSelector</c> owns both directions of the format so there is one definition of
    /// it. Null rather than empty for a handler that returns a single type: an empty set would be a
    /// response set with no cases, which is not the same thing and is not a shape anything produces.
    /// </para>
    /// </remarks>
    public string? UnionCases { get; set; }

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
    ///
    /// <para>
    /// <see cref="UnionCases"/> was added to this in the same edit that added the property, for that
    /// reason. A case set changing while this string does not is a caching failure that surfaces as
    /// a handler dispatching on cases it no longer declares, which looks like nothing to do with
    /// caching at all.
    /// </para>
    /// </remarks>
    public override string ToString() {
        return $"{IsAsync}:{OutputType}:{RawResponseContentType}:{StreamFraming}:{ReturnType}" +
               $":{DefaultStatusCode}:{NullResponseBodyExpression}:{ProducedContentTypes}" +
               $":{UnionCases}";
    }
}