namespace Hardened.Requests.Runtime.Authorization;

/// <summary>
/// The application's default posture.
/// </summary>
public interface IAuthorizationConfiguration {
    /// <summary>
    /// Whether a handler carrying no authorization attribute is denied rather than public.
    /// </summary>
    bool RequireAuthorization { get; }
}

/// <inheritdoc cref="IAuthorizationConfiguration" />
public class AuthorizationConfiguration : IAuthorizationConfiguration {
    /// <summary>
    /// False, so existing applications are unaffected: every handler stays public until somebody
    /// opts in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the runtime half of the opt-in, and it is a backstop rather than the whole feature.
    /// It catches handlers that arrive from a referenced assembly no source generator analysed, which
    /// is the case a build-time check structurally cannot see.
    /// </para>
    /// <para>
    /// The half that makes forgetting an attribute a build error rather than a 403 somebody finds in
    /// staging needs syntax, and only a generator has that.
    /// </para>
    /// </remarks>
    public bool RequireAuthorization { get; set; }
}
