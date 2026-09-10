namespace Hardened.Generation.Models;

/// <summary>
/// A base URL the contract says the service is served from, for the published document's
/// <c>servers</c> list.
/// </summary>
/// <remarks>
/// Where the application is deployed is the one thing a generated document cannot derive from the
/// code, and a contract is one of the two places an author can say it - <c>[Server]</c> on the
/// entry point is the other. A contract's <c>servers</c> block was read for its path component and
/// otherwise dropped, so a specification-first document published no <c>servers</c> at all and a
/// client generated from it had a set of paths and nowhere to send them.
/// </remarks>
internal class ServerModel : IEquatable<ServerModel> {

    /// <summary>The URL as the contract wrote it, less any base path the build already applied.</summary>
    public string Url { get; set; } = "";

    public string? Description { get; set; }

    public bool Equals(ServerModel? other) =>
        other is not null && Url == other.Url && Description == other.Description;

    public override bool Equals(object? obj) => Equals(obj as ServerModel);

    public override int GetHashCode() {
        unchecked {
            return (Url.GetHashCode() * 397) ^ (Description?.GetHashCode() ?? 0);
        }
    }
}
