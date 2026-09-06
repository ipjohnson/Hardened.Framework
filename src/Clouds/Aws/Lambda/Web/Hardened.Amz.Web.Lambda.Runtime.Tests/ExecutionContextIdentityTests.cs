using Hardened.Amz.Web.Lambda.Runtime.Tests.Infrastructure;
using Hardened.Requests.Abstract.Authorization;
using Xunit;

namespace Hardened.Amz.Web.Lambda.Runtime.Tests;

/// <summary>
/// The two things a request carries about who made it and what to file it under.
///
/// <para>
/// Both arrived on <c>IExecutionContext</c> with Hardened.Framework 0.5.0, so every transport has
/// to implement them and every transport can implement them subtly differently. These assert the
/// two properties that actually matter at run time: a caller is never absent, and a fork is the
/// same request rather than a new one.
/// </para>
/// </summary>
public class ExecutionContextIdentityTests {

    /// <summary>
    /// A request nobody has authenticated has an anonymous caller rather than a null one.
    /// </summary>
    /// <remarks>
    /// "No credential was presented" is a value, not an absence, which is what lets every reader
    /// downstream skip a null check. A transport that left this null would turn the first
    /// authorization check on that transport into a NullReferenceException.
    /// </remarks>
    [Fact]
    public async Task ARequestStartsWithAnAnonymousCaller() {
        var harness = new ApiGatewayHarness();

        await harness.Process(ApiGatewayHarness.Event());

        Assert.NotNull(harness.ExecutionContext.CallerPrincipal);
        Assert.False(harness.ExecutionContext.CallerPrincipal.IsAuthenticated);
    }

    /// <summary>
    /// The correlation id is present and stable, rather than a fresh value per read.
    /// </summary>
    /// <remarks>
    /// It is realized lazily, so reading it twice has to give the same answer - otherwise every
    /// log line for one request would carry a different id, which is precisely the failure the
    /// identifier exists to prevent.
    /// </remarks>
    [Fact]
    public async Task TheCorrelationIdIsStableAcrossReads() {
        var harness = new ApiGatewayHarness();

        await harness.Process(ApiGatewayHarness.Event());

        var first = harness.ExecutionContext.CorrelationId;
        var second = harness.ExecutionContext.CorrelationId;

        Assert.False(string.IsNullOrEmpty(first));
        Assert.Equal(first, second);
    }

    /// <summary>
    /// A clone is the same caller, by reference.
    /// </summary>
    /// <remarks>
    /// A fork exists so a filter can re-run a handler against a cloned context, and a retry is the
    /// same caller - so the clone must observe the same principal, including a later revocation.
    /// Copying the value would let a fork outlive a credential that stopped being valid.
    /// </remarks>
    [Fact]
    public async Task ACloneCarriesTheCaller() {
        var harness = new ApiGatewayHarness();

        await harness.Process(ApiGatewayHarness.Event());

        var context = harness.ExecutionContext;

        context.CallerPrincipal = new CallerPrincipal("bearer", ["pets:read"]);

        var clone = context.Clone(null, null, null, null);

        Assert.Same(context.CallerPrincipal, clone.CallerPrincipal);
    }

    /// <summary>
    /// And the same request, so it reports one id rather than two.
    /// </summary>
    /// <remarks>
    /// A forked chain that generated a second id would split one request's logs in two, which is
    /// the thing that makes an incident unreadable exactly when it is being read.
    /// </remarks>
    [Fact]
    public async Task ACloneCarriesTheCorrelationId() {
        var harness = new ApiGatewayHarness();

        await harness.Process(ApiGatewayHarness.Event());

        var context = harness.ExecutionContext;

        var clone = context.Clone(null, null, null, null);

        Assert.Equal(context.CorrelationId, clone.CorrelationId);
    }
}
