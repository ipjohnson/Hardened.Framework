using Hardened.Web.Runtime.CacheControl;
using Xunit;

namespace Hardened.Web.Runtime.Tests.CacheControl;

/// <summary>
/// What each combination of <see cref="CacheControlEnum"/> renders as.
/// </summary>
/// <remarks>
/// The directives are what a cache reads, so the exact string matters more than usual: an extra
/// comma or a missing <c>public</c> changes what a CDN does with the response.
/// </remarks>
public class CacheControlHeaderTests {

    [Fact]
    public void TheDefaultCombinationIsAPublicMaxAge() {
        Assert.Equal(
            "public, max-age=0",
            CacheControlHeader.Format(CacheControlEnum.MaxAge | CacheControlEnum.Public, 0));
    }

    [Fact]
    public void MaxAgeCarriesItsValue() {
        Assert.Equal(
            "public, max-age=86400",
            CacheControlHeader.Format(CacheControlEnum.MaxAge | CacheControlEnum.Public, 86400));
    }

    /// <summary>
    /// The flag decides whether <c>max-age</c> appears, not the number. Without that,
    /// <c>no-store</c> on its own would still emit a <c>max-age=0</c> nobody asked for.
    /// </summary>
    [Fact]
    public void MaxAgeIsOmittedWhenItsFlagIsNotSet() {
        Assert.Equal("no-store", CacheControlHeader.Format(CacheControlEnum.NoStore, 3600));
    }

    [Fact]
    public void EveryDirectiveHasARendering() {
        Assert.Equal("no-cache", CacheControlHeader.Format(CacheControlEnum.NoCache, 0));
        Assert.Equal("no-store", CacheControlHeader.Format(CacheControlEnum.NoStore, 0));
        Assert.Equal("no-transform", CacheControlHeader.Format(CacheControlEnum.NoTransform, 0));
        Assert.Equal("public", CacheControlHeader.Format(CacheControlEnum.Public, 0));
        Assert.Equal("private", CacheControlHeader.Format(CacheControlEnum.Private, 0));
    }

    /// <summary>
    /// <c>public</c> and <c>private</c> contradict each other, and the enum is a flags type that
    /// cannot stop both being set. The more restrictive one is the safer reading.
    /// </summary>
    [Fact]
    public void PrivateWinsOverPublicWhenBothAreSet() {
        Assert.Equal(
            "private",
            CacheControlHeader.Format(CacheControlEnum.Public | CacheControlEnum.Private, 0));
    }

    /// <summary>
    /// A contradictory pair is still rendered as written. The framework's job is to say what the
    /// author declared, not to decide which half was meant.
    /// </summary>
    [Fact]
    public void ContradictoryDirectivesAreBothRendered() {
        Assert.Equal(
            "no-store, max-age=60",
            CacheControlHeader.Format(CacheControlEnum.NoStore | CacheControlEnum.MaxAge, 60));
    }

    [Fact]
    public void ImmutableIsAppended() {
        Assert.Equal(
            "public, max-age=31536000, immutable",
            CacheControlHeader.Format(
                CacheControlEnum.MaxAge | CacheControlEnum.Public, 31536000, immutable: true));
    }

    /// <summary>
    /// No directive means no header, rather than an empty one.
    /// </summary>
    [Fact]
    public void NoDirectivesRendersNothing() {
        Assert.Null(CacheControlHeader.Format(default, 0));
    }

    /// <summary>
    /// The order is fixed so a response's header does not depend on the order the flags happen to
    /// be combined in.
    /// </summary>
    [Fact]
    public void DirectiveOrderDoesNotDependOnFlagOrder() {
        var one = CacheControlHeader.Format(
            CacheControlEnum.NoTransform | CacheControlEnum.MaxAge | CacheControlEnum.Private, 30);
        var other = CacheControlHeader.Format(
            CacheControlEnum.Private | CacheControlEnum.NoTransform | CacheControlEnum.MaxAge, 30);

        Assert.Equal("private, max-age=30, no-transform", one);
        Assert.Equal(one, other);
    }
}
