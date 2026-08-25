namespace Hardened.Generation.Models;

internal class ServiceSpecModel : IEquatable<ServiceSpecModel> {
    public string FileName { get; set; } = "";
    public List<SchemaModel> Schemas { get; set; } = new();
    public List<ServiceModel> Services { get; set; } = new();
    public List<FilterTypeModel> FilterTypes { get; set; } = new();

    /// <summary>
    /// What the build task called this spec's <c>IJsonTypeInfoResolver</c>.
    /// </summary>
    /// <remarks>
    /// Carried across rather than recomputed, so the routing table registers the name that was
    /// actually emitted. Deriving it independently on both sides is how the two would drift.
    /// </remarks>
    public string JsonTypeInfoResolverName { get; set; } = "";

    /// <summary>
    /// Where this specification is served, or empty when it is not.
    /// </summary>
    /// <remarks>
    /// From <c>PublishUrl</c> metadata on the spec item. A specification-first application already
    /// has its contract as a build input, so where it publishes it is a fact about the file rather
    /// than about the entry point - which is why it travels with the file rather than being restated
    /// as an attribute the two could disagree about.
    /// </remarks>
    public string PublishUrl { get; set; } = "";

    /// <summary>
    /// Where the reference page for this specification is served, or empty when there is none.
    /// </summary>
    /// <remarks>
    /// From <c>UiUrl</c> metadata. A page renders exactly one document, so naming it beside the
    /// document it renders is what keeps the two from drifting.
    /// </remarks>
    public string UiUrl { get; set; } = "";

    /// <summary>
    /// Where the contract file itself is served, from <c>SourceUrl</c> metadata. Empty by default.
    /// </summary>
    /// <remarks>
    /// Separate from <c>PublishUrl</c>, which serves the document generated from the model. The two
    /// answer different questions: the generated document says what the build understood, and the
    /// source says what the author wrote - including the comments, examples, vendor extensions and
    /// ordering that no model represents, and including anything the front end dropped. Publishing
    /// the second is a deliberate choice rather than the default it used to be, and it is only
    /// meaningful where the source is a document a client can read.
    /// </remarks>
    public string SourceUrl { get; set; } = "";

    /// <summary>
    /// The environments the reference page is served in, comma separated, or empty for all of them.
    /// </summary>
    /// <remarks>
    /// From <c>UiEnvironments</c> metadata, and passed straight to <c>HardenedOpenApiUi</c> - so a
    /// specification-first page is gated exactly the way an attribute-declared one is, rather than
    /// being the one route in an application that cannot be.
    /// </remarks>
    public string UiEnvironments { get; set; } = "";

    /// <summary>
    /// What the build task emitted for validation, per operation. Empty when nothing is constrained.
    /// </summary>
    public List<ValidatedOperationModel> ValidatedOperations { get; set; } = new();

    /// <summary>
    /// <c>x-hardened-content-negotiation</c> at the document root - "strict", "lenient", or empty.
    /// </summary>
    /// <remarks>
    /// A whole-service answer rather than a per-operation one, and at the root because that is the
    /// only place in a document that addresses the service. What an operation produces is per
    /// operation; what happens outside that set is not.
    /// </remarks>
    public string ContentNegotiation { get; set; } = "";

    /// <summary>
    /// How this spec's handlers declare their responses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The build task chooses it from <c>$(HardenedResponseModel)</c> and the emitters read it, but
    /// it also has to survive into the generator: the interface signature and the dispatch that
    /// fills it are written by two different halves of the build, and the second reads only this
    /// model. Without it the generator emitted a plain assignment for a handler whose signature
    /// returned a response set - so the wrapper went on the wire, under its own Value member, at
    /// whatever status the operation would have answered anyway.
    /// </para>
    /// <para>
    /// At the root rather than per operation, because it is what the module asked for. Whether a
    /// given operation ends up with a response set is a different question and belongs to
    /// <c>ResponseSetPlan.RequiresResponseSet</c>, which reads this and the operation both.
    /// </para>
    /// </remarks>
    public SpecResponseModel ResponseModel { get; set; } = SpecResponseModel.Standard;

    /// <summary>
    /// Keywords the description declared that the parser did not map, in the order they were met.
    /// </summary>
    /// <remarks>
    /// Filled by the parser and read by <c>SpecDiagnostics</c>, which is the only reason it hangs
    /// off the model at all: that pass takes a model and nothing else, and a dropped keyword is
    /// invisible in one by definition. It is not serialized and it is deliberately absent from
    /// <see cref="Equals(ServiceSpecModel?)"/> - a keyword nobody mapped generates no C#, so two
    /// models differing only here generate the same code and must not miss a cache hit over it.
    /// </remarks>
    public List<UnmappedKeywordModel> UnmappedKeywords { get; set; } = new();

    public bool Equals(ServiceSpecModel? other) {
        if (other is not null && ContentNegotiation != other.ContentNegotiation) return false;
        if (other is not null && ResponseModel != other.ResponseModel) return false;

        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (FileName != other.FileName) return false;
        if (JsonTypeInfoResolverName != other.JsonTypeInfoResolverName) return false;
        if (PublishUrl != other.PublishUrl) return false;
        if (UiUrl != other.UiUrl) return false;
        if (SourceUrl != other.SourceUrl) return false;
        if (UiEnvironments != other.UiEnvironments) return false;
        if (Schemas.Count != other.Schemas.Count) return false;
        if (Services.Count != other.Services.Count) return false;
        if (FilterTypes.Count != other.FilterTypes.Count) return false;
        if (ValidatedOperations.Count != other.ValidatedOperations.Count) return false;

        for (var i = 0; i < ValidatedOperations.Count; i++) {
            if (!ValidatedOperations[i].Equals(other.ValidatedOperations[i])) return false;
        }

        for (var i = 0; i < Schemas.Count; i++) {
            if (!Schemas[i].Equals(other.Schemas[i])) return false;
        }

        for (var i = 0; i < Services.Count; i++) {
            if (!Services[i].Equals(other.Services[i])) return false;
        }

        for (var i = 0; i < FilterTypes.Count; i++) {
            if (!FilterTypes[i].Equals(other.FilterTypes[i])) return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as ServiceSpecModel);

    public override int GetHashCode() {
        unchecked {
            var hash = FileName.GetHashCode();
            foreach (var s in Schemas) hash = (hash * 397) ^ s.GetHashCode();
            foreach (var s in Services) hash = (hash * 397) ^ s.GetHashCode();
            foreach (var f in FilterTypes) hash = (hash * 397) ^ f.GetHashCode();
            return hash;
        }
    }
}
