using Hardened.Requests.Abstract.RequestFilter;

namespace Hardened.Requests.Abstract.Tests.RequestFilter;

/// <summary>
/// Where the two authorization slots sit relative to everything else.
///
/// <para>
/// These are constants, so nothing stops one being edited to a value that still compiles and still
/// runs - and the failure is not an exception but a filter quietly running at the wrong point.
/// Authentication landing after serialization means a request with no credential deserializes its
/// body before being rejected; authorization landing before validation means a requirement over
/// bound parameters reads parameters that are not there yet. Both are the kind of defect that
/// reaches production intact, so the relationships are asserted rather than the values.
/// </para>
/// </summary>
public class FilterOrderTests {

    /// <summary>
    /// The reason authentication is early: a request presenting no credential must not cause a
    /// 10 MB body to be read before it is rejected.
    /// </summary>
    [Fact]
    public void AuthenticationRunsBeforeAnythingIsDeserialized() {
        Assert.True(FilterOrder.Authentication < FilterOrder.BeforeSerialization);
        Assert.True(FilterOrder.Authentication < FilterOrder.Serialization);
    }

    /// <summary>
    /// A requirement over grants alone is authorized at <c>Authentication + 1</c>, so that slot has
    /// to exist and still be ahead of serialization. This is why the gap is two rather than one -
    /// the arithmetic, not the literal, is the thing that matters.
    /// </summary>
    [Fact]
    public void TheSlotAfterAuthenticationIsStillAheadOfSerialization() {
        Assert.True(FilterOrder.Authentication + 1 < FilterOrder.BeforeSerialization);
    }

    /// <summary>
    /// The other position: a requirement over the resource reads bound parameters, which do not
    /// exist until deserialization and validation have run.
    /// </summary>
    [Fact]
    public void AuthorizationRunsAfterParametersAreBound() {
        Assert.True(FilterOrder.Authorization > FilterOrder.Serialization);
        Assert.True(FilterOrder.Authorization > FilterOrder.Validation);
    }

    /// <summary>
    /// Both authorization positions are ahead of the handler. Either running after the endpoint has
    /// been invoked would authorize work that has already happened.
    /// </summary>
    [Fact]
    public void BothAuthorizationPositionsRunBeforeTheHandler() {
        Assert.True(FilterOrder.Authentication + 1 < FilterOrder.EndPointInvoke);
        Assert.True(FilterOrder.Authorization < FilterOrder.EndPointInvoke);
    }

    /// <summary>
    /// Handler creation stays first. Authentication running before it would have no handler to read
    /// metadata from, and the accepted schemes for an operation are metadata.
    /// </summary>
    [Fact]
    public void HandlerCreationStillPrecedesAuthentication() {
        Assert.True(FilterOrder.HandlerCreation < FilterOrder.Authentication);
    }

    /// <summary>
    /// Neither new value collides with an existing one. A collision is not an error - the sort is
    /// stable and both filters would run - but it makes the relative order an accident of
    /// registration rather than a decision.
    /// </summary>
    [Fact]
    public void NeitherAuthorizationSlotCollidesWithAnExistingStage() {
        int[] existing = [
            FilterOrder.HandlerCreation,
            FilterOrder.BeforeSerialization,
            FilterOrder.Serialization,
            FilterOrder.Validation,
            FilterOrder.DefaultValue,
            FilterOrder.EndPointInvoke,
        ];

        Assert.DoesNotContain(FilterOrder.Authentication, existing);
        Assert.DoesNotContain(FilterOrder.Authentication + 1, existing);
        Assert.DoesNotContain(FilterOrder.Authorization, existing);
    }
}
