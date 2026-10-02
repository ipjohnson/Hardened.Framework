namespace Hardened.SourceGenerator.OpenApiDocument;

/// <summary>
/// The members of a contract's <c>info</c> beyond its title, version and description.
/// </summary>
/// <remarks>
/// <para>
/// Only the title, version and description were carried, so a contract's <c>license</c> and
/// <c>contact</c> never reached the published document and a linter reading it warned
/// <c>info-license</c> about a contract that declared one.
/// </para>
/// <para>
/// <see cref="Summary"/> and <see cref="LicenseIdentifier"/> are OpenAPI 3.1 only, so they are
/// fields the writer can leave out of a 3.0 document rather than members of an opaque object.
/// </para>
/// </remarks>
public sealed class DocumentInfo : System.IEquatable<DocumentInfo>
{
    public DocumentInfo(
        string? summary,
        string? termsOfService,
        string? contactJson,
        string? licenseName,
        string? licenseIdentifier,
        string? licenseUrl,
        string? extensionsJson
    )
    {
        Summary = summary;
        TermsOfService = termsOfService;
        ContactJson = contactJson;
        LicenseName = licenseName;
        LicenseIdentifier = licenseIdentifier;
        LicenseUrl = licenseUrl;
        ExtensionsJson = extensionsJson;
    }

    public string? Summary { get; }

    public string? TermsOfService { get; }

    /// <summary>The <c>contact</c> object as JSON.</summary>
    public string? ContactJson { get; }

    public string? LicenseName { get; }

    public string? LicenseIdentifier { get; }

    public string? LicenseUrl { get; }

    /// <summary>The <c>x-</c> members as comma-separated JSON members.</summary>
    public string? ExtensionsJson { get; }

    public bool Equals(DocumentInfo? other) =>
        other is not null
        && Summary == other.Summary
        && TermsOfService == other.TermsOfService
        && ContactJson == other.ContactJson
        && LicenseName == other.LicenseName
        && LicenseIdentifier == other.LicenseIdentifier
        && LicenseUrl == other.LicenseUrl
        && ExtensionsJson == other.ExtensionsJson;

    public override bool Equals(object? obj) => Equals(obj as DocumentInfo);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = Summary?.GetHashCode() ?? 0;
            hash = (hash * 397) ^ (ContactJson?.GetHashCode() ?? 0);
            hash = (hash * 397) ^ (LicenseName?.GetHashCode() ?? 0);
            return hash;
        }
    }
}
