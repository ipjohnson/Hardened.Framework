namespace Hardened.Idl.Models;

internal class OperationModel : IEquatable<OperationModel> {
    public string OperationId { get; set; } = "";

    /// <summary>
    /// The C# name this operation's method, handler and parameter interface are built from.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="OperationId"/>, which is the document's own and stays as written -
    /// it is what a reader matches against and what the code-first direction round-trips. Cloudflare
    /// declares both <c>DeleteWebhook</c> and <c>deleteWebhook</c>, so the two ids are distinct and
    /// the two C# names have to be made so.
    /// </remarks>
    public string MethodName {
        get => _methodName.Length > 0 ? _methodName : Idl.NamingHelper.ToPascalCase(OperationId);
        set => _methodName = value ?? "";
    }

    private string _methodName = "";
    public string Path { get; set; } = "";
    public string HttpMethod { get; set; } = "";

    /// <summary>
    /// The exact token this operation is dispatched on, or null to route by path and method.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An RPC protocol wearing HTTP as an envelope does not route: every operation is one method on
    /// one path, and which operation it is comes from somewhere else. awsJson1_0 is POST / with an
    /// <c>X-Amz-Target</c> header naming <c>Service.Operation</c>, and the header value is this.
    /// </para>
    /// <para>
    /// It is deliberately not a protocol enum. What the routing table needs to know is whether an
    /// operation is selected by an exact token and what that token is - not which specification
    /// invented the idea. <see cref="Path"/> and <see cref="HttpMethod"/> keep their values either
    /// way, because a request still has to arrive at POST / for anything to dispatch at all.
    /// </para>
    /// </remarks>
    public string? DispatchKey { get; set; }

    public string? Tag { get; set; }

    /// <summary>The spec's <c>deprecated</c>, which becomes <c>[Obsolete]</c>.</summary>
    public bool IsDeprecated { get; set; }

    /// <summary>
    /// The operation's <c>summary</c>, or its <c>description</c> where it has no summary, as the
    /// generated method's doc comment.
    /// </summary>
    public string? Description { get; set; }
    public List<ParameterModel> Parameters { get; set; } = new();
    public string? RequestBodyContentType { get; set; }
    public string? RequestBodyRef { get; set; }
    public string? RequestBodyType { get; set; }

    /// <summary>
    /// The media type the response schema was read from - "application/json", "text/plain",
    /// "text/html". Null when the operation declares no response content.
    /// </summary>
    public string? ResponseContentType { get; set; }

    /// <summary>
    /// The schema of one item of a streamed response, from OpenAPI 3.2's <c>itemSchema</c>.
    /// </summary>
    /// <remarks>
    /// Set instead of <see cref="ResponseRef"/> rather than beside it, because the two say
    /// different things: <c>schema</c> describes the whole body, <c>itemSchema</c> describes one of
    /// many. A generator reading both would have to guess which the response actually is.
    /// </remarks>
    public string? ItemSchemaRef { get; set; }

    public string? ResponseRef { get; set; }
    public string? ResponseType { get; set; }
    public string? ResponseFormat { get; set; }
    public bool ResponseIsArray { get; set; }
    public string? ResponseArrayItemsRef { get; set; }

    /// <summary>
    /// The array's element type, where the elements are primitives rather than a <c>$ref</c>.
    /// </summary>
    /// <remarks>
    /// Only the <c>$ref</c> was carried, so <c>items: {type: string}</c> left the mapper with
    /// nothing to name and every array-of-primitives response became <c>JsonElement</c>.
    /// Array-of-<c>$ref</c> worked, which is why it went unnoticed.
    /// </remarks>
    public string? ResponseArrayItemsType { get; set; }

    /// <summary>The element's <c>format</c>, which distinguishes int32 from int64 and date from date-time.</summary>
    public string? ResponseArrayItemsFormat { get; set; }
    public int SuccessStatusCode { get; set; } = 200;

    /// <summary>
    /// The non-2xx responses the specification declares, in status order.
    /// </summary>
    /// <remarks>
    /// The 2xx response keeps the flat fields above rather than joining this list. Every consumer
    /// of the success response reads those individually, and moving them would rewrite all of them
    /// for no gain - the success case is one response by construction, and these are the rest.
    /// </remarks>
    public List<ErrorResponseModel> ErrorResponses { get; set; } = new();

    /// <summary>
    /// Every media type the success response declares, in document order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Distinct from <see cref="ResponseContentType"/>, which is the one media type the schema was
    /// read from - JSON where the operation offers it. That one decides the C# return type; this one
    /// is the set the response is negotiated against, and an operation offering both JSON and
    /// plain text has one of the first and two of the second.
    /// </para>
    /// <para>
    /// Document order matters: it is what an <c>Accept</c> of <c>*/*</c> resolves to, because the
    /// first representation a document lists is the one it leads with.
    /// </para>
    /// </remarks>
    public List<string> ProducedContentTypes { get; set; } = new();

    /// <summary>
    /// Opt in to a <c>byte[]</c> signature for a response the spec types as a string.
    /// </summary>
    /// <remarks>
    /// The default stays <c>string</c>, which is what <c>type: string</c> means and what a caller
    /// reading the document expects. <c>byte[]</c> is a performance choice about a payload the
    /// application already holds encoded: RawOutputHelper writes a byte[] straight to the body,
    /// where a string is UTF-8 encoded into a fresh array on every request. It only pays when the
    /// value is cached - encoding per request just moves the allocation - so it is the author's
    /// call rather than something inferred from the schema.
    /// </remarks>
    public bool RawBytesResponse { get; set; }

    // x-filters: typed filter attribute instances applied to this operation
    public List<FilterInstanceModel> FilterInstances { get; set; } = new();

    // Validation: body schema properties for validation filter generation
    public List<PropertyModel> RequestBodyProperties { get; set; } = new();
    public List<string> RequestBodyRequired { get; set; } = new();

    public bool HasValidationConstraints {
        get {
            foreach (var p in Parameters) {
                if (p.HasValidationConstraints) return true;
            }
            foreach (var p in RequestBodyProperties) {
                if (p.HasValidationConstraints) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Compares every field the generator reads.
    /// </summary>
    /// <remarks>
    /// This model is an incremental-pipeline cache key: Roslyn compares a freshly parsed value
    /// against the cached one to decide whether the downstream emit runs. Until this covered the
    /// response and request-body fields it compared only the operation's identity and its
    /// parameters, so editing a response schema, its media type or its status code produced a model
    /// that compared equal to the previous one and the generator served the code it had already
    /// emitted. The spec said one thing and the build kept shipping another.
    /// </remarks>
    public bool Equals(OperationModel? other) {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return OperationId == other.OperationId && Path == other.Path &&
               HttpMethod == other.HttpMethod && DispatchKey == other.DispatchKey &&
               Tag == other.Tag &&
               Description == other.Description && IsDeprecated == other.IsDeprecated &&
               SuccessStatusCode == other.SuccessStatusCode &&
               RequestBodyContentType == other.RequestBodyContentType &&
               RequestBodyRef == other.RequestBodyRef &&
               RequestBodyType == other.RequestBodyType &&
               ResponseContentType == other.ResponseContentType &&
               ResponseRef == other.ResponseRef &&
               ResponseType == other.ResponseType &&
               ResponseFormat == other.ResponseFormat &&
               ResponseIsArray == other.ResponseIsArray &&
               ResponseArrayItemsRef == other.ResponseArrayItemsRef &&
               ResponseArrayItemsType == other.ResponseArrayItemsType &&
               ResponseArrayItemsFormat == other.ResponseArrayItemsFormat &&
               ErrorResponses.SequenceEqual(other.ErrorResponses) &&
               ProducedContentTypes.SequenceEqual(other.ProducedContentTypes) &&
               Parameters.SequenceEqual(other.Parameters) &&
               FilterInstances.SequenceEqual(other.FilterInstances) &&
               RequestBodyProperties.SequenceEqual(other.RequestBodyProperties) &&
               RequestBodyRequired.SequenceEqual(other.RequestBodyRequired);
    }

    public override bool Equals(object? obj) => Equals(obj as OperationModel);

    /// <summary>
    /// Deliberately narrower than <see cref="Equals(OperationModel?)"/> - identity only, over three
    /// fields that never change for a given operation. Roslyn buckets by hash and then compares, so
    /// a hash must agree with equality but need not distinguish everything equality does.
    /// </summary>
    public override int GetHashCode() {
        unchecked {
            var hash = OperationId.GetHashCode();
            hash = (hash * 397) ^ Path.GetHashCode();
            hash = (hash * 397) ^ HttpMethod.GetHashCode();
            return hash;
        }
    }
}
