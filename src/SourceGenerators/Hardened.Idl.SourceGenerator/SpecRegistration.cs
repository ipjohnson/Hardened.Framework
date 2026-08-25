namespace Hardened.Idl.SourceGenerator;

/// <summary>
/// What one specification contributes to an application's registrations.
/// </summary>
/// <remarks>
/// <para>
/// One value per spec rather than one collected array per fact, because each array costs a
/// <c>.Combine</c> in the incremental pipeline and the three travel together anyway - all of them are
/// read from the same spec by the same build task.
/// </para>
/// <para>
/// A record, so the incremental cache compares it by value. Every member is a string for that
/// reason: the pipeline keys on equality, and a type that compared by reference would rebuild the
/// routing table on every keystroke.
/// </para>
/// </remarks>
/// <param name="ResolverName">
/// The fully qualified <c>IJsonTypeInfoResolver</c> the build task emitted, or empty when the spec
/// declared no schemas.
/// </param>
/// <param name="SpecificationTypeName">
/// The fully qualified class carrying the embedded document, or empty when it was not embedded.
/// </param>
/// <param name="PublishUrl">
/// Where the document is served, from <c>PublishUrl</c> metadata, or empty when it is not served.
/// </param>
/// <param name="UiUrl">
/// Where its reference page is served, from <c>UiUrl</c> metadata, or empty when there is none.
/// </param>
/// <param name="UiEnvironments">
/// The environments that page is served in, from <c>UiEnvironments</c> metadata, or empty for all
/// of them. Passed to <c>HardenedOpenApiUi</c> unchanged, so a specification-first page is gated
/// the same way an attribute-declared one is.
/// </param>
internal record SpecRegistration(
    string ResolverName,
    string SpecificationTypeName,
    string PublishUrl,
    string SourceUrl,
    string UiUrl,
    string UiEnvironments,

    /// <summary>
    /// <c>x-hardened-content-negotiation</c> from this description's root, or empty.
    /// </summary>
    string ContentNegotiation);
