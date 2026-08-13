namespace Hardened.OpenApi.SourceGenerator.Models;

internal class OperationModel : IEquatable<OperationModel> {
    public string OperationId { get; set; } = "";
    public string Path { get; set; } = "";
    public string HttpMethod { get; set; } = "";
    public string? Tag { get; set; }
    public List<ParameterModel> Parameters { get; set; } = new();
    public string? RequestBodyContentType { get; set; }
    public string? RequestBodyRef { get; set; }
    public string? RequestBodyType { get; set; }

    /// <summary>
    /// The media type the response schema was read from - "application/json", "text/plain",
    /// "text/html". Null when the operation declares no response content.
    /// </summary>
    public string? ResponseContentType { get; set; }

    public string? ResponseRef { get; set; }
    public string? ResponseType { get; set; }
    public string? ResponseFormat { get; set; }
    public bool ResponseIsArray { get; set; }
    public string? ResponseArrayItemsRef { get; set; }
    public int SuccessStatusCode { get; set; } = 200;

    /// <summary>
    /// The view this operation's model is rendered through, from <c>x-hardened-template</c>. Null
    /// for an operation that serializes rather than renders.
    /// </summary>
    public string? TemplateName { get; set; }

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
               HttpMethod == other.HttpMethod && Tag == other.Tag &&
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
               TemplateName == other.TemplateName &&
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
