using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Runtime.Tests.Support;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Execution;

/// <summary>
/// What a filter ahead of serialization can read off a request that was already turned away.
/// </summary>
/// <remarks>
/// H-29 from the 0.19.0-rc1000 trial. Everything ahead of <c>FilterOrder.Serialization</c> refuses
/// by recording and calling <c>Next</c>, so a counting filter placed inside a transport rate limit
/// still ran for every refused request, and one placed outside did too. Position alone could not
/// express "count an authorization refusal, not a transport refusal"; all three arms ended up
/// reading <c>ExceptionValue</c> by hand.
/// </remarks>
public class RefusedTests {

    [Fact]
    public async Task ARequestNothingRefusedIsNotRefused() {
        var context = Pipeline.Context();
        bool? seen = null;

        await Pipeline.Chain(context, new Pipeline.Inline(chain => {
            seen = chain.Context.Response.Refused;

            return Task.CompletedTask;
        })).Next();

        Assert.False(seen);
    }

    [Fact]
    public async Task ARefusalRecordedAheadIsVisibleToTheNextFilter() {
        var context = Pipeline.Context();
        bool? seen = null;
        Exception? which = null;

        await Pipeline.Chain(context,
            new Pipeline.Inline(chain => {
                chain.Context.Response.ExceptionValue =
                    new AuthorizationException(AuthorizationChallenge.AuthenticationRequired(), "no credential");

                return chain.Next();
            }),
            new Pipeline.Inline(chain => {
                seen = chain.Context.Response.Refused;
                which = chain.Context.Response.ExceptionValue;

                return Task.CompletedTask;
            })).Next();

        Assert.True(seen);
        Assert.IsType<AuthorizationException>(which);
    }
}
