using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Responses;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Tests.Responses;

/// <summary>
/// Throwing a response with its case type intact.
///
/// <para>
/// The whole of what the generic form adds over <c>ResponseException</c> is a typed catch. Without
/// it a caller catches the base and tests <c>e.Response is NotFound</c> - a check the compiler
/// cannot make and a rename does not reach.
/// </para>
/// </summary>
public class TypedResponseExceptionTests {

    /// <summary>
    /// The point of it. A catch for one case does not catch another, which is what makes the type
    /// parameter worth having rather than decorative.
    /// </summary>
    [Fact]
    public void ACatchForOneCaseDoesNotCatchAnother() {
        var caught = false;

        try {
            throw new ResponseException<NotFound>(new NotFound("todo"));
        }
        catch (ResponseException<Conflict>) {
            Assert.Fail("A Conflict catch must not catch a NotFound.");
        }
        catch (ResponseException<NotFound> e) {
            caught = true;
            Assert.Equal("todo", e.Response.Resource);
        }

        Assert.True(caught);
    }

    /// <summary>
    /// Deriving from the non-generic form is what keeps a catch-all working, so an existing handler
    /// that catches ResponseException does not stop seeing these.
    /// </summary>
    [Fact]
    public void TheBaseCatchStillCatchesIt() {
        try {
            throw new ResponseException<Gone>(new Gone());
        }
        catch (ResponseException e) {
            Assert.IsType<Gone>(e.Response);
        }
    }

    /// <summary>
    /// And the pipeline's own path is unchanged: the converter matches the interface and reads the
    /// body off StatusCodeException.Value, both inherited.
    /// </summary>
    [Fact]
    public void ItIsStillAStatusCodeExceptionCarryingItsBody() {
        var response = new NotFound("todo", "No todo with id 7.");
        var exception = new ResponseException<NotFound>(response);

        Assert.IsAssignableFrom<IStatusCodeException>(exception);
        Assert.IsAssignableFrom<StatusCodeException>(exception);
        Assert.Equal(404, exception.StatusCode);
        Assert.Same(response, exception.Value);
    }

    /// <summary>
    /// The typed property and the base one are the same instance, so the two never disagree about
    /// what was thrown.
    /// </summary>
    [Fact]
    public void TheTypedAndUntypedResponseAreTheSameInstance() {
        var response = new Conflict("clash");
        var exception = new ResponseException<Conflict>(response);

        Assert.Same(response, exception.Response);
        Assert.Same(response, ((ResponseException)exception).Response);
    }

    /// <summary>
    /// Headers still come from the response, so a typed throw and a returned value produce the same
    /// bytes.
    /// </summary>
    [Fact]
    public void HeadersStillComeFromTheResponse() {
        var response = new Unauthorized(Challenge: AuthorizationChallenge.InvalidToken());

        var returned = new Dictionary<string, StringValues>();
        var thrown = new Dictionary<string, StringValues>();

        response.ApplyHeaders(returned);
        new ResponseException<Unauthorized>(response).ApplyHeaders(thrown);

        Assert.Equal(returned, thrown);
    }

    /// <summary>
    /// A bodyless case still carries no body, so a typed throw of a 204 does not start sending one.
    /// </summary>
    [Fact]
    public void ABodylessCaseStillCarriesNoBody() {
        Assert.Null(new ResponseException<NoContent>(new NoContent()).Value);
    }

    #region the extension

    /// <summary>
    /// The type argument is inferred, so the case type is named once rather than twice.
    /// </summary>
    [Fact]
    public void AsExceptionInfersTheCaseType() {
        var exception = new NotFound("todo").AsException();

        Assert.IsType<ResponseException<NotFound>>(exception);
        Assert.Equal(404, exception.StatusCode);
    }

    [Fact]
    public void AsExceptionCarriesAMessageWhenGivenOne() {
        var exception = new Gone().AsException("The board was deleted in March.");

        Assert.Equal("The board was deleted in March.", exception.Message);
    }

    [Fact]
    public void AsExceptionNamesTheStatusWhenGivenNoMessage() {
        Assert.Contains("410", new Gone().AsException().Message, StringComparison.Ordinal);
    }

    #endregion
}
