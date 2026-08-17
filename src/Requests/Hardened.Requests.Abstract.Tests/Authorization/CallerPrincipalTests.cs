using Hardened.Requests.Abstract.Authorization;

namespace Hardened.Requests.Abstract.Tests.Authorization;

/// <summary>
/// The caller a request runs as.
///
/// <para>
/// Two invariants carry the design and are worth pinning: <c>IsAuthenticated</c> is derived rather
/// than stored, so there is no way to build a principal that lies about it; and the value is
/// genuinely immutable, so handing one to a filter cannot be a way to edit it.
/// </para>
/// </summary>
public class CallerPrincipalTests {

    #region anonymous

    /// <summary>
    /// One instance, shared. It holds nothing and answers nothing, so there is no per-request state
    /// to get wrong under concurrency.
    /// </summary>
    [Fact]
    public void Anonymous_IsASingleSharedInstance() {
        Assert.Same(AnonymousCallerPrincipal.Instance, AnonymousCallerPrincipal.Instance);
    }

    [Fact]
    public void Anonymous_IsNotAuthenticatedAndNamesNoScheme() {
        Assert.Null(AnonymousCallerPrincipal.Instance.AuthenticationScheme);
        Assert.False(AnonymousCallerPrincipal.Instance.IsAuthenticated);
    }

    /// <summary>
    /// Empty rather than null, which is what lets a policy walk the same code path for an anonymous
    /// caller as for an authenticated one instead of null-checking first.
    /// </summary>
    [Fact]
    public void Anonymous_HoldsNoGrantsAndNoClaims() {
        Assert.Empty(AnonymousCallerPrincipal.Instance.Grants);
        Assert.False(AnonymousCallerPrincipal.Instance.TryGetClaim("sub", out _));
        Assert.Null(AnonymousCallerPrincipal.Instance.Subject);
        Assert.Null(AnonymousCallerPrincipal.Instance.Issuer);
    }

    #endregion

    #region authenticated

    /// <summary>
    /// Derived from the scheme, not stored beside it. The invalid states - authenticated with no
    /// scheme, or a scheme without being authenticated - are unrepresentable rather than merely
    /// undocumented.
    ///
    /// <para>
    /// Read through the interface because that is where the derivation lives, and there is nowhere
    /// else it could live and still be one rule: a class supplying its own would be free to disagree
    /// with the scheme it carries. It is also how every real caller sees a principal, since the
    /// execution context exposes the interface.
    /// </para>
    /// </summary>
    [Fact]
    public void IsAuthenticated_FollowsFromTheSchemeBeingPresent() {
        ICallerPrincipal principal = new CallerPrincipal("bearer");

        Assert.True(principal.IsAuthenticated);
    }

    [Fact]
    public void Constructing_WithoutASchemeThrows() {
        Assert.Throws<ArgumentException>(() => new CallerPrincipal(""));
        Assert.Throws<ArgumentException>(() => new CallerPrincipal(null!));
    }

    [Fact]
    public void Constructing_CarriesSubjectIssuerAndGrants() {
        var principal = new CallerPrincipal(
            "bearer",
            ["pets:read", "pets:write"],
            subject: "user-42",
            issuer: "https://issuer.example");

        Assert.Equal("bearer", principal.AuthenticationScheme);
        Assert.Equal("user-42", principal.Subject);
        Assert.Equal("https://issuer.example", principal.Issuer);
        Assert.Contains("pets:read", principal.Grants);
        Assert.Contains("pets:write", principal.Grants);
    }

    [Fact]
    public void Constructing_WithNoGrantsYieldsAnEmptySetRatherThanNull() {
        Assert.Empty(new CallerPrincipal("bearer").Grants);
    }

    /// <summary>
    /// The immutability is structural, not a convention. A grant set the caller can still reach
    /// through the interface and cast back to a live <c>HashSet</c> would make "the value never
    /// mutates" false while looking true.
    /// </summary>
    [Fact]
    public void Grants_AreNotWritableThroughTheSetHandedIn() {
        var source = new HashSet<string> { "pets:read" };
        var principal = new CallerPrincipal("bearer", source);

        source.Add("admin:*");

        Assert.DoesNotContain("admin:*", principal.Grants);
    }

    #endregion

    #region claims

    [Fact]
    public void TryGetClaim_ReadsAClaimTheCredentialCarried() {
        var principal = new CallerPrincipal(
            "bearer", claims: [new KeyValuePair<string, string>("tenant", "acme")]);

        Assert.True(principal.TryGetClaim("tenant", out var value));
        Assert.Equal("acme", value);
    }

    [Fact]
    public void TryGetClaim_ReportsFalseForAClaimThatIsNotThere() {
        Assert.False(new CallerPrincipal("bearer").TryGetClaim("tenant", out var value));
        Assert.Null(value);
    }

    /// <summary>
    /// A claim name is a wire identifier, so it is matched ordinally for the same reason a grant is.
    /// </summary>
    [Fact]
    public void TryGetClaim_MatchesTheNameCaseSensitively() {
        var principal = new CallerPrincipal(
            "bearer", claims: [new KeyValuePair<string, string>("tenant", "acme")]);

        Assert.False(principal.TryGetClaim("Tenant", out _));
    }

    /// <summary>
    /// A token can present the same claim twice. Throwing would turn a token that some other
    /// validator accepts into a 500; taking the last one is what a dictionary build would do anyway.
    /// </summary>
    [Fact]
    public void Constructing_WithADuplicateClaimKeepsTheLastRatherThanThrowing() {
        var principal = new CallerPrincipal("bearer", claims: [
            new KeyValuePair<string, string>("tenant", "first"),
            new KeyValuePair<string, string>("tenant", "second"),
        ]);

        Assert.True(principal.TryGetClaim("tenant", out var value));
        Assert.Equal("second", value);
    }

    #endregion
}
