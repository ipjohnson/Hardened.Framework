namespace Hardened.Requests.Abstract.Serializer;

/// <summary>
/// Matching between a media type a client asked for and one a serializer produces.
/// </summary>
/// <remarks>
/// <para>
/// One implementation, deliberately. Every response serializer used to decide this for itself with
/// <c>context.Request.Accept?.Contains("application/json")</c>, and that substring test is false for
/// <c>*/*</c> and for a request with no <c>Accept</c> header at all - which is most of them. Those
/// requests were served only because the JSON serializer is also the default, so the fallback meant
/// for genuine mismatches was quietly carrying the common case.
/// </para>
/// </remarks>
public static class MediaType {
    /// <summary>The wildcard a client sends when it will take anything.</summary>
    public const string Any = "*/*";

    /// <summary>
    /// Whether <paramref name="produced"/> satisfies a client asking for <paramref name="requested"/>.
    /// </summary>
    /// <param name="requested">
    /// One entry from an <c>Accept</c> header: a concrete type, a subtype wildcard such as
    /// <c>text/*</c>, or <c>*/*</c>.
    /// </param>
    /// <param name="produced">The concrete media type a serializer writes.</param>
    public static bool Matches(string? requested, string? produced) {
        if (string.IsNullOrEmpty(produced)) {
            return false;
        }

        // An absent Accept header means the client will take anything, which is the same answer as
        // */* rather than a reason to refuse.
        if (string.IsNullOrEmpty(requested) || requested == Any) {
            return true;
        }

        if (string.Equals(requested, produced, StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        var slash = requested!.IndexOf('/');

        // "text/*" against "text/html". Anything without a slash is not a media type; treated as no
        // match rather than guessed at.
        if (slash < 0 || slash != requested.Length - 2 || requested[requested.Length - 1] != '*') {
            return false;
        }

        return produced!.Length > slash &&
               produced[slash] == '/' &&
               string.Compare(requested, 0, produced, 0, slash, StringComparison.OrdinalIgnoreCase) == 0;
    }
}
