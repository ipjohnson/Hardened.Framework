using Hardened.Requests.Abstract.Caching;
using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Tests.Caching;

/// <summary>
/// What a declaration that answers only what the interface always asked for means.
/// </summary>
/// <remarks>
/// <see cref="ICacheResponseDeclaration.Scope"/> and <see cref="ICacheResponseDeclaration.Tags"/>
/// have default implementations so that a declaration written before either existed still compiles.
/// These pin what that compiles to: nothing said about who the answer is for, which is a failure
/// naming the handler on anything guarded, and no tag, which is an entry only its duration removes.
/// </remarks>
public class CacheResponseDeclarationTests {

    /// <summary>
    /// Through the interface, which is the only way a default implementation is reachable and the
    /// way everything asking a declaration these questions holds one.
    /// </summary>
    private static readonly ICacheResponseDeclaration Minimal = new MinimalDeclaration();

    [Fact]
    public void ADeclarationThatSaysNothingAboutItsAudienceIsUnstated() {
        Assert.Equal(CacheScope.Unstated, Minimal.Scope);
    }

    [Fact]
    public void ADeclarationThatNamesNoTagsHasNone() {
        Assert.Empty(Minimal.Tags);
    }

    /// <summary>
    /// Everything the interface has always required, and nothing it has since added.
    /// </summary>
    private sealed class MinimalDeclaration : ICacheResponseDeclaration {
        public int Duration => 0;

        public ICacheKeyProvider CreateKeyProvider() => new EveryRequest();

        private sealed class EveryRequest : ICacheKeyProvider {
            public static ICacheKeyProvider Create(string[] values) => new EveryRequest();

            public ValueTask<string?> Key(IExecutionContext context) => new("only");
        }
    }
}
