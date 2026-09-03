using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Responses;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Tests.Responses;

/// <summary>
/// The statuses the shipped set gained, and the two things about them that are not shared.
/// </summary>
/// <remarks>
/// <para>
/// The rest is covered by reflection: <see cref="BuiltInResponseTypeTests"/> checks every
/// <c>[HttpStatus]</c> type's status agreement and sealing, and <see cref="GenericResponseTests"/>
/// checks that each generic problem carries the same <c>type</c> URI and title as its bare form.
/// Restating those per status here would be a list that says nothing a reader could not get from
/// the declaration.
/// </para>
/// <para>
/// What is not shared is the header each of the three header-writing additions sends, and which of
/// them serialize nothing at all. Both are wire behaviour and neither is checkable by reflection.
/// </para>
/// </remarks>
public class WidenedResponseSetTests {

    private static Dictionary<string, StringValues> Applied(IProvidesResponseHeaders response) {
        var headers = new Dictionary<string, StringValues>();

        response.ApplyHeaders(headers);

        return headers;
    }

    #region headers

    /// <summary>
    /// A 405 without <c>Allow</c> is correct and useless: the header is the only part of the
    /// response that tells the caller what to do instead. Required in the constructor for that
    /// reason, so this is the whole of the behaviour.
    /// </summary>
    [Fact]
    public void MethodNotAllowed_SendsTheMethodsThatAreAllowed() {
        Assert.Equal("GET, HEAD", Applied(new MethodNotAllowed("GET, HEAD"))[KnownHeaders.Allow]);
    }

    /// <summary>And the generic form sends it too, which is the copy that could have been missed.</summary>
    [Fact]
    public void AGenericMethodNotAllowedSendsItToo() {
        Assert.Equal(
            "GET, HEAD",
            Applied(new MethodNotAllowed<string>("nope", "GET, HEAD"))[KnownHeaders.Allow]);
    }

    /// <summary>
    /// A 304 answered from <c>If-None-Match</c> repeats the validator the client sent.
    /// </summary>
    [Fact]
    public void NotModified_SendsTheETagItWasGiven() {
        Assert.Equal("\"v1\"", Applied(new NotModified("\"v1\""))[KnownHeaders.ETag]);
    }

    /// <summary>
    /// And one answered from <c>If-Modified-Since</c> may have no entity tag to repeat. Inventing
    /// one would tell the client it can revalidate against a value the store does not know.
    /// </summary>
    [Fact]
    public void NotModified_WithNoETagSendsNoHeader() {
        Assert.Empty(Applied(new NotModified()));
    }

    #endregion

    #region what carries no body

    /// <summary>
    /// Three of the additions serialize nothing, and each for its own reason rather than by
    /// omission: 304 because RFC 9110 forbids a body, 405 because the response is the status and
    /// the header, 406 because anything written would be in a media type the client has just said
    /// it cannot read.
    /// </summary>
    [Fact]
    public void TheBodylessAdditionsSerializeNothing() {
        Assert.False(HasBody(new NotModified()));
        Assert.False(HasBody(new MethodNotAllowed("GET")));
        Assert.False(HasBody(new NotAcceptable()));
    }

    /// <summary>
    /// And the rest carry the problem document their bare form describes, which is what makes them
    /// worth having over the <c>ErrorModel</c> a bodyless status used to answer with.
    /// </summary>
    [Fact]
    public void TheProblemAdditionsCarryABody() {
        Assert.True(HasBody(new UnprocessableContent()));
        Assert.True(HasBody(new InternalServerError()));
        Assert.True(HasBody(new ContentTooLarge()));
        Assert.True(HasBody(new GatewayTimeout()));
    }

    /// <summary>
    /// The generic forms send the payload rather than themselves. Wrapping it would put the
    /// caller's document under a <c>Body</c> member and ship the wrapper's own fields beside it.
    /// </summary>
    [Fact]
    public void AGenericAdditionCarriesItsBodyRatherThanItself() {
        Assert.Equal(
            "detail", ((ICarriesResponseBody)new UnprocessableContent<string>("detail")).Body);

        Assert.Equal(
            "detail", ((ICarriesResponseBody)new MethodNotAllowed<string>("detail", "GET")).Body);
    }

    private static bool HasBody(IHttpStatusResponse response) => response.HasBody;

    #endregion
}
