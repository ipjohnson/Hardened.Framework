using Hardened.SourceGenerator.Requests;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Requests;

/// <summary>
/// Which operations a declaration written on a class reaches.
/// </summary>
/// <remarks>
/// A filter that stands down on some of the operations under it leaves the document claiming a
/// status or a header those operations cannot produce. <c>[ConditionalGet]</c> on a controller
/// installs on the reads, so a 304 on the writes beside them is a lie a generated client would
/// write a branch for. The declaration states its reach and this applies it, which keeps the rule
/// that nothing in the selector knows what any filter does.
/// </remarks>
public class DeclaredScopeTests {

    [Fact]
    public void AnUnrestrictedDeclarationReachesEveryOperation() {
        var scope = new DeclaredScope(methods: null, notWhenStreaming: false);

        Assert.True(scope.Reaches("GET", streams: false));
        Assert.True(scope.Reaches("POST", streams: false));
        Assert.True(scope.Reaches("GET", streams: true));
    }

    [Fact]
    public void AMethodRestrictionReachesOnlyThose() {
        var scope = new DeclaredScope("GET,HEAD", notWhenStreaming: false);

        Assert.True(scope.Reaches("GET", streams: false));
        Assert.True(scope.Reaches("HEAD", streams: false));
        Assert.False(scope.Reaches("POST", streams: false));
        Assert.False(scope.Reaches("DELETE", streams: false));
    }

    /// <summary>Written the way somebody writes it.</summary>
    [Fact]
    public void SpacingAndCaseDoNotDecideIt() {
        var scope = new DeclaredScope(" get , Head ", notWhenStreaming: false);

        Assert.True(scope.Reaches("GET", streams: false));
        Assert.True(scope.Reaches("head", streams: false));
    }

    [Fact]
    public void AStreamingHandlerIsLeftOutWhereTheDeclarationSaysSo() {
        var restricted = new DeclaredScope("GET", notWhenStreaming: true);

        Assert.True(restricted.Reaches("GET", streams: false));
        Assert.False(restricted.Reaches("GET", streams: true));

        var unrestricted = new DeclaredScope("GET", notWhenStreaming: false);

        Assert.True(unrestricted.Reaches("GET", streams: true));
    }

    /// <summary>
    /// An operation whose verb is unknown keeps the declaration rather than losing it.
    /// </summary>
    /// <remarks>
    /// The narrowing exists to stop a document claiming what an operation cannot answer. Dropping
    /// a declaration because the verb was not carried would lose what it can answer, which is the
    /// worse of the two.
    /// </remarks>
    [Fact]
    public void AnUnknownVerbKeepsTheDeclaration() =>
        Assert.True(new DeclaredScope("GET", notWhenStreaming: false).Reaches(null, streams: false));
}
