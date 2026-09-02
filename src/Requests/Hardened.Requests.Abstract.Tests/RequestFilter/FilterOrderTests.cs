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
    /// A requirement over grants alone is authorized at <see cref="FilterOrder.GrantAuthorization"/>,
    /// which has to be after a caller exists and still ahead of serialization.
    /// </summary>
    /// <remarks>
    /// This was spelled <c>Authentication + 1</c> on both sides until the stage got a name. A
    /// position documented in three files and expressed nowhere is one nothing can sit beside.
    /// </remarks>
    [Fact]
    public void GrantAuthorizationRunsAfterAuthenticationAndAheadOfSerialization() {
        Assert.True(FilterOrder.GrantAuthorization > FilterOrder.Authentication);
        Assert.True(FilterOrder.GrantAuthorization < FilterOrder.Serialization);
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
        Assert.True(FilterOrder.GrantAuthorization < FilterOrder.EndPointInvoke);
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
    /// Neither authorization slot collides with another stage. A collision is not an error - the
    /// sort is stable, so both filters run in registration order - but between two <em>stages</em>
    /// it would make the relative order an accident of registration rather than a decision. Two
    /// filters deliberately sharing a position through <see cref="FilterOrder.Before"/> or
    /// <see cref="FilterOrder.After"/> is the case that is fine.
    /// </summary>
    [Fact]
    public void NeitherAuthorizationSlotCollidesWithAnExistingStage() {
        int[] existing = [
            FilterOrder.HandlerCreation,
            FilterOrder.RateLimitTransport,
            FilterOrder.RateLimitPrincipal,
            FilterOrder.Conditional,
            FilterOrder.ResponseCache,
            FilterOrder.BeforeSerialization,
            FilterOrder.Serialization,
            FilterOrder.Validation,
            FilterOrder.Retry,
            FilterOrder.DefaultValue,
            FilterOrder.EndPointInvoke,
        ];

        Assert.DoesNotContain(FilterOrder.Authentication, existing);
        Assert.DoesNotContain(FilterOrder.GrantAuthorization, existing);
        Assert.DoesNotContain(FilterOrder.Authorization, existing);
    }
}
