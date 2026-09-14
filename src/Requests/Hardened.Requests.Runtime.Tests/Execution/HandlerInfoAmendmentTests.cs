using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Timeouts;
using Hardened.Requests.Runtime.Execution;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Execution;

/// <summary>
/// The amendments that carry every other member of a handler across unchanged.
/// </summary>
/// <remarks>
/// Each one answers the handler it was given when there is nothing to change, which is what makes
/// them free for the handlers nothing spoke about - every handler in an application that declares
/// no convention, no wider rung and no module base path. Reference equality is the assertion,
/// because a copy that happened to be equal would still be an allocation per handler built.
/// </remarks>
public class HandlerInfoAmendmentTests
{
    private static ExecutionRequestHandlerInfo Handler(
        Requirement? requirement = null,
        TimeoutPolicy? timeout = null,
        IReadOnlyList<object>? metadata = null
    ) =>
        new(
            "/orders",
            "GET",
            typeof(HandlerInfoAmendmentTests),
            "Get",
            metadata: metadata,
            requirement: requirement,
            timeout: timeout
        );

    [Fact]
    public void ARequirementNothingStatedLeavesTheHandlerAlone()
    {
        var handler = Handler();

        Assert.Same(handler, handler.WithRequirement(null));
    }

    /// <summary>
    /// The one it already carries. Conventions are asked per handler and most answer what the
    /// handler already said.
    /// </summary>
    [Fact]
    public void TheRequirementItAlreadyCarriesLeavesTheHandlerAlone()
    {
        var requirement = Requirement.Grant("orders:read");
        var handler = Handler(requirement);

        Assert.Same(handler, handler.WithRequirement(requirement));
    }

    [Fact]
    public void ANewRequirementIsCarriedAndNothingElseChanges()
    {
        var handler = Handler(Requirement.Grant("orders:read"));
        var stated = Requirement.Grant("orders:write");

        var amended = handler.WithRequirement(stated);

        Assert.NotSame(handler, amended);
        Assert.Same(stated, amended.Requirement);
        Assert.Equal("/orders", amended.Path);
        Assert.Equal("GET", amended.Method);
        Assert.Equal("Get", amended.InvokeMethod);
    }

    [Fact]
    public void ATimeoutNothingStatedLeavesTheHandlerAlone()
    {
        var handler = Handler(timeout: new TimeoutPolicy(2000));

        Assert.Same(handler, handler.WithTimeout(null));
    }

    /// <summary>
    /// Equality rather than reference: a rung resolves to a policy built where it was read, and a
    /// handler already bounded by the same numbers is already correct.
    /// </summary>
    [Fact]
    public void ATimeoutEqualToTheOneItCarriesLeavesTheHandlerAlone()
    {
        var handler = Handler(timeout: new TimeoutPolicy(2000));

        Assert.Same(handler, handler.WithTimeout(new TimeoutPolicy(2000)));
    }

    [Fact]
    public void ADifferentTimeoutIsCarriedAndNothingElseChanges()
    {
        var handler = Handler(timeout: new TimeoutPolicy(2000));
        var amended = handler.WithTimeout(new TimeoutPolicy(500));

        Assert.NotSame(handler, amended);
        Assert.Equal(500, amended.Timeout!.Milliseconds);
        Assert.Equal("/orders", amended.Path);
    }

    [Fact]
    public void NoWiderRungLeavesTheHandlerAlone()
    {
        var handler = Handler();

        Assert.Same(handler, handler.WithWiderRungs(Array.Empty<object>()));
    }

    /// <summary>
    /// Appended, because nearest-first is what <c>TimeoutFrom</c> and <c>RequirementFrom</c> read.
    /// </summary>
    [Fact]
    public void AWiderRungIsAppendedToWhatTheHandlerAlreadyDeclared()
    {
        var own = new object();
        var wider = new object();

        var amended = Handler(metadata: new[] { own }).WithWiderRungs(new[] { wider });

        Assert.Equal(new[] { own, wider }, amended.Metadata);
    }
}
