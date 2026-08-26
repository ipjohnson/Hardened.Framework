using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Responses;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Tests.Responses;

/// <summary>
/// The three gaps nine independent applications each worked around.
/// </summary>
/// <remarks>
/// <para>
/// <c>BuiltInResponseTypeTests</c> already covers status agreement, sealing and the interface for
/// every type by reflection, so nothing here restates those. What is asserted is the behaviour each
/// of these was added for.
/// </para>
/// <para>
/// The absences were measured rather than assumed. Every application in the study invented its own
/// 400 shape; every optimistic-concurrency route answered one of its two conditional cases with a
/// status it had to choose itself; and every route that needed a header on a 200 hand-wrote a filter
/// to put one there.
/// </para>
/// </remarks>
public class AddedResponseTypeTests {

    private sealed record Part(string Sku);

    #region Ok<T> - a 200 whose headers are part of the answer

    /// <summary>
    /// The case this exists for: an <c>ETag</c> on a read, set by the handler that computed it
    /// rather than by a filter re-deriving it from the payload afterwards.
    /// </summary>
    [Fact]
    public void Ok_AppliesTheHeadersItWasGiven() {
        var headers = new Dictionary<string, StringValues>();

        new Ok<Part>(new Part("A1"), KnownHeaders.ETag, "\"v1\"").ApplyHeaders(headers);

        Assert.Equal("\"v1\"", headers[KnownHeaders.ETag]);
    }

    [Fact]
    public void Ok_AppliesEveryHeaderInTheDictionary() {
        var headers = new Dictionary<string, StringValues>();

        new Ok<Part>(new Part("A1"), new Dictionary<string, string> {
            [KnownHeaders.ETag] = "\"v1\"",
            [KnownHeaders.CacheControl] = "no-cache"
        }).ApplyHeaders(headers);

        Assert.Equal("\"v1\"", headers[KnownHeaders.ETag]);
        Assert.Equal("no-cache", headers[KnownHeaders.CacheControl]);
    }

    /// <summary>
    /// Headers are optional, so the plain form stays a plain 200 rather than a null dereference.
    /// </summary>
    [Fact]
    public void Ok_WithNoHeadersAppliesNone() {
        var headers = new Dictionary<string, StringValues>();

        new Ok<Part>(new Part("A1")).ApplyHeaders(headers);

        Assert.Empty(headers);
    }

    /// <summary>
    /// Assigns rather than appends, which is the contract <c>IProvidesResponseHeaders</c> states: a
    /// retried or forked request producing the same response twice must not send the header twice.
    /// </summary>
    [Fact]
    public void Ok_AssignsRatherThanAppends() {
        var headers = new Dictionary<string, StringValues>();
        var response = new Ok<Part>(new Part("A1"), KnownHeaders.ETag, "\"v1\"");

        response.ApplyHeaders(headers);
        response.ApplyHeaders(headers);

        Assert.Equal("\"v1\"", headers[KnownHeaders.ETag]);
    }

    /// <summary>
    /// The body is the value, not the wrapper - the same rule <c>Created&lt;T&gt;</c> follows.
    /// Serializing the record would put the caller's resource under a <c>value</c> member and the
    /// headers in the body as well as on the response.
    /// </summary>
    [Fact]
    public void Ok_SendsTheValueAsTheBodyRatherThanItself() {
        var part = new Part("A1");

        Assert.Same(part, ((ICarriesResponseBody)new Ok<Part>(part)).Body);
    }

    #endregion

    #region the conditional-request pair

    /// <summary>
    /// 428 is the half that was missing, and it is the half that loses updates: a validator that was
    /// never sent, rather than one that is stale.
    /// </summary>
    [Fact]
    public void PreconditionRequired_Is428AndPairsWith412() {
        Assert.Equal(428, new PreconditionRequired().Status);
        Assert.Equal(412, new PreconditionFailed().Status);
    }

    [Fact]
    public void PreconditionRequired_CarriesItsOwnProblemType() {
        Assert.Equal(ProblemTypes.PreconditionRequired, new PreconditionRequired().Type);
        Assert.NotEqual(new PreconditionFailed().Type, new PreconditionRequired().Type);
    }

    [Fact]
    public void PreconditionRequiredOfT_SendsTheSuppliedBody() {
        var part = new Part("A1");

        Assert.Same(part, ((ICarriesResponseBody)new PreconditionRequired<Part>(part)).Body);
    }

    /// <summary>
    /// The generic and non-generic forms are one problem kind, so they share a <c>type</c> URI. RFC
    /// 9457 makes <c>type</c> the identity of what went wrong, not of what shape the body is.
    /// </summary>
    [Fact]
    public void PreconditionRequired_BothFormsShareOneProblemType() {
        Assert.Equal(
            new PreconditionRequired().Type,
            new PreconditionRequired<Part>(new Part("A1")).Type);
    }

    #endregion

    #region BadRequest

    [Fact]
    public void BadRequest_Is400WithItsOwnProblemType() {
        Assert.Equal(400, new BadRequest().Status);
        Assert.Equal(ProblemTypes.BadRequest, new BadRequest().Type);
    }

    [Fact]
    public void BadRequest_BothFormsShareOneProblemType() {
        Assert.Equal(new BadRequest().Type, new BadRequest<Part>(new Part("A1")).Type);
    }

    [Fact]
    public void BadRequestOfT_SendsTheSuppliedBody() {
        var part = new Part("A1");

        Assert.Same(part, ((ICarriesResponseBody)new BadRequest<Part>(part)).Body);
    }

    #endregion

    /// <summary>
    /// Every built-in problem type is a distinct URI. A duplicate would make two different problems
    /// indistinguishable to a client matching on <c>type</c>, which is the member RFC 9457 asks it to
    /// match on.
    /// </summary>
    [Fact]
    public void EveryProblemTypeUriIsDistinct() {
        var uris = typeof(ProblemTypes)
            .GetFields()
            .Where(field => field.IsLiteral && field.Name != nameof(ProblemTypes.Prefix))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

        Assert.Equal(uris.Length, uris.Distinct().Count());
    }
}
