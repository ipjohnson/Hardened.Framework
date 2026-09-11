using CSharpAuthor;

namespace Hardened.SourceGenerator.Models.Request;

public record ResponseInformationModel {
    public bool IsAsync { get; set; }

    public bool IsAsyncEnumerable { get; set; }

    public ITypeDefinition? AsyncEnumerableItemType { get; set; }

    public ITypeDefinition? ReturnType { get; set; }

    /// <summary>
    /// Whether the returned value can carry response headers of its own.
    /// </summary>
    /// <remarks>
    /// A response set reaches <c>ApplyHeaders</c> through the switch its cases are dispatched on.
    /// A handler returning one thing has no switch, so the call has to be emitted beside the
    /// assignment - and only where the type could satisfy it, because <c>is</c> against a type that
    /// provably cannot implement an interface is CS8121 rather than a test that returns false.
    /// </remarks>
    public bool ReturnTypeProvidesHeaders { get; set; }

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
    /// C# naming the body the contract declares for each status it names, or null where it declares
    /// none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The dictionary literal the handler info takes, filled from the same generated fields
    /// <see cref="NullResponseBodyExpression"/> names. What a refusal the pipeline raised writes:
    /// the exception carries no body, so only the contract can say what shape the status answers
    /// with.
    /// </para>
    /// <para>
    /// One string for the reason <see cref="ProducedContentTypes"/> is one - this is a
    /// <c>record</c>, and a collection member would compare by reference and never invalidate the
    /// incremental cache.
    /// </para>
    /// </remarks>
    public string? DeclaredErrorBodiesExpression { get; set; }

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

    /// <summary>
    /// The media types the success responses declare, comma-joined, or null where nothing said.
    /// </summary>
    /// <remarks>
    /// Spec-first only, and only where it differs from <see cref="ProducedContentTypes"/>. A
    /// described operation's negotiated set carries its error representations as well as its
    /// success ones - see <c>OperationModel.SuccessContentTypes</c> - so describing the success
    /// with it published <c>application/json</c> on a <c>text/plain</c> success. Code-first the two
    /// cannot differ: <c>[Produces]</c> is a statement about the response only, and the exception
    /// path negotiates within whatever it named.
    /// </remarks>
    public string? SuccessContentTypes { get; set; }

    /// <summary>
    /// The media types the error responses declare, comma-joined, or null where nothing said.
    /// </summary>
    /// <remarks>
    /// Spec-first only, for the same reason, and it is the exact answer where it is set: a contract
    /// states what its refusals look like. Code-first the document writer works it out from the
    /// declared set and the return type, because nothing states it.
    /// </remarks>
    public string? ErrorContentTypes { get; set; }

    /// <summary>
    /// The content type put on the response before the handler runs, or empty for a handler that
    /// negotiates.
    /// </summary>
    /// <remarks>
    /// Set from <c>[Produces]</c> where the operation names exactly one media type and the handler
    /// returns <c>byte[]</c>, <c>Stream</c> or <c>string</c> - the return types <c>[RawResponse]</c>
    /// could be written on, which is what keeps this behaviour identical for every handler that
    /// carried it.
    /// </remarks>
    public string? RawResponseContentType { get; set; }

    /// <summary>
    /// Whether the handler returns <c>byte[]</c> or <c>Stream</c>, so nothing can serialize its
    /// response.
    /// </summary>
    /// <remarks>
    /// The return type is the declaration: returning either says the handler controls its own
    /// serialization. The pass-through writer is bound when the pipeline is composed, and no
    /// serializer is consulted on any request. A <c>string</c> is not one of these - it has a JSON
    /// reading, and takes the writer by declaring a media type instead.
    /// </remarks>
    public bool WritesRawBytes { get; set; }

    /// <summary>
    /// Whether the handler's return value is already what goes on the wire, so no serializer
    /// structures it: <see cref="WritesRawBytes"/> and <c>string</c>.
    /// </summary>
    /// <remarks>
    /// Wider than <see cref="WritesRawBytes"/> by exactly <c>string</c>, and carried separately
    /// because the two answer different questions. That one decides whether the pass-through writer
    /// is bound; this one decides whether an error model can go out under the operation's declared
    /// media type. It cannot: an error model is a model, and
    /// <c>RawResponseSerializer.CanProduce</c> refuses anything that is not already bytes, so the
    /// exception path commits JSON. The document writer reads this to describe error bodies the way
    /// the runtime answers them.
    /// </remarks>
    public bool ReturnsBytesOrText { get; set; }

    /// <summary>
    /// The status the contract declares validation failures answer with, or null for the stock
    /// 400.
    /// </summary>
    /// <remarks>
    /// Set only from a described operation declaring 422, which is the one status that names
    /// validation refusal. The attribute that would have carried this code-first was removed
    /// because a hand-written assertion has no source of truth behind it; a contract does, and
    /// arm C of the second trial declared exactly this and was answered 400 anyway.
    /// </remarks>
    public int? ValidationErrorStatus { get; set; }

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
    /// What is wrong with the declared case set, encoded, or null where nothing is.
    /// </summary>
    /// <remarks>
    /// Found in the syntax transform, where the symbols exist, and reported from the routing
    /// generator, where a <c>SourceProductionContext</c> does. Carried here for the same reason
    /// <see cref="StreamFraming"/> is carried rather than rejected in place.
    /// </remarks>
    public string? UnionDiagnostic { get; set; }

    /// <summary>
    /// <c>[Throws&lt;T&gt;]</c> declarations naming a type with no status and stating none.
    /// </summary>
    /// <remarks>
    /// Carried rather than reported where it is found, for the same reason UnionDiagnostic is: the
    /// model is built in a syntax transform, which has no SourceProductionContext to report into.
    /// The emit step has one and reports it there.
    /// </remarks>
    public string? ThrowsDiagnostic { get; set; }

    /// <summary>
    /// The framing named on a handler that has no stream to frame, or null where the two agree.
    /// </summary>
    /// <remarks>
    /// Carried rather than reported where it is found, for the reason <see cref="UnionDiagnostic"/>
    /// is, and reported from the routing generator as <c>HRDW004</c>. The emitter branches on the
    /// return type first and ignores the framing on such a handler, so without the report the
    /// author would get a buffered JSON response and a document that says so.
    /// </remarks>
    public string? StreamFramingDiagnostic { get; set; }

    /// <summary>
    /// Whether the handler returns bytes and declares no content type.
    /// </summary>
    /// <remarks>
    /// Carried rather than reported where it is found, for the reason
    /// <see cref="StreamFramingDiagnostic"/> is, and reported from the routing generator as
    /// <c>HRDR011</c>.
    /// </remarks>
    public bool MissingContentTypeDiagnostic { get; set; }

    /// <summary>
    /// The declared media types nothing in this compilation writes, comma-joined, or null.
    /// </summary>
    /// <remarks>
    /// Reported from the routing generator as <c>HRDR012</c>, a warning - see
    /// <c>ContentTypeDiagnostics</c> for why it is not an error.
    /// </remarks>
    public string? UnproducibleContentTypeDiagnostic { get; set; }

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
    /// The single case a handler returning a response type on its own declares, encoded, or null.
    /// </summary>
    /// <remarks>
    /// <see cref="UnionCases"/> for a handler that has no set. A type like <c>Created&lt;T&gt;</c>
    /// states its status, its headers and which member is its body whether or not a set is written
    /// around it, and this is where that statement is carried so the dispatch and the document read
    /// the same one.
    /// </remarks>
    public string? DeclaredResponse { get; set; }

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
        return $"{IsAsync}:{OutputType}:{RawResponseContentType}:{WritesRawBytes}" +
               $":{ReturnsBytesOrText}" +
               $":{StreamFraming}:{ReturnType}" +
               $":{DefaultStatusCode}:{NullResponseBodyExpression}:{DeclaredErrorBodiesExpression}" +
               $":{ProducedContentTypes}:{SuccessContentTypes}:{ErrorContentTypes}" +
               $":{UnionCases}:{DeclaredResponse}:{UnionDiagnostic}:{ThrowsDiagnostic}" +
               $":{ValidationErrorStatus}" +
               $":{StreamFramingDiagnostic}" +
               $":{MissingContentTypeDiagnostic}:{UnproducibleContentTypeDiagnostic}";
    }
}