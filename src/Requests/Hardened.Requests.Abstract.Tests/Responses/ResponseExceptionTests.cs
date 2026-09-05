using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Responses;

namespace Hardened.Requests.Abstract.Tests.Responses;

/// <summary>
/// Throwing a response type, which is how these reach the wire before the response modes exist.
///
/// <para>
/// A handler in throws mode returns one type and has nowhere to put a second, so without this the
/// built-in types would be declarations and nothing else until the union work landed. What makes it
/// work is that <c>ExceptionToModelConverter</c> already answers an <c>IStatusCodeException</c> with
/// its status and its headers, and reads a declared body off <c>StatusCodeException.Value</c> - so
/// the tests here are about landing in all three of those places, not about the exception itself.
/// </para>
/// </summary>
public class ResponseExceptionTests {

    [Fact]
    public void StatusCode_ComesFromTheResponse() {
        Assert.Equal(404, new ResponseException(new NotFound("todo")).StatusCode);
        Assert.Equal(409, new ResponseException(new Conflict()).StatusCode);
        Assert.Equal(503, new ResponseException(new ServiceUnavailable()).StatusCode);
    }

    /// <summary>
    /// It must be the response and not an <c>ErrorModel</c>. The converter reads the declared body
    /// off this property specifically, so getting it wrong produces the right status with the
    /// response silently discarded - which is the failure that looks like it works.
    /// </summary>
    [Fact]
    public void Value_IsTheResponseItself() {
        var response = new NotFound("todo", "No todo with id 7.");

        Assert.Same(response, new ResponseException(response).Value);
    }

    /// <summary>
    /// A record that carries a body hands that body over, not itself. Returned, the generated
    /// dispatch sends a <c>NotFound&lt;T&gt;</c>'s payload; thrown, it used to send the wrapper with
    /// the payload nested under <c>body</c>, so the same answer had two shapes. The template's
    /// specification-first throws mode was the case: its 404 declared a Problem and shipped a
    /// wrapper around one.
    /// </summary>
    [Fact]
    public void Value_IsTheCarriedBodyForAResponseThatCarriesOne() {
        var problem = new { Detail = "No todo with id 7." };

        Assert.Same(problem, new ResponseException(new NotFound<object>(problem)).Value);
        Assert.Same(problem, new ResponseException(new Created<object>(problem, "/todos/7")).Value);
    }

    /// <summary>
    /// A 204 or a 202 must not carry <c>{}</c>. The converter falls back to its own error model when
    /// Value is null, so a bodyless response has to be null here rather than merely empty.
    /// </summary>
    [Fact]
    public void Value_IsNullForABodylessResponse() {
        Assert.Null(new ResponseException(new NoContent()).Value);
        Assert.Null(new ResponseException(new Accepted("/jobs/7")).Value);
    }

    [Fact]
    public void Response_IsAvailableWithoutUnwrappingValue() {
        var response = new Gone();

        Assert.Same(response, new ResponseException(response).Response);
    }

    /// <summary>
    /// The interface is what ExceptionToModelConverter matches on, ahead of its type-based
    /// classification. Failing to satisfy it would land these in the "not a client error, so 500"
    /// branch.
    /// </summary>
    [Fact]
    public void ResponseException_IsAStatusCodeException() {
        Assert.IsAssignableFrom<IStatusCodeException>(new ResponseException(new Forbidden()));
        Assert.IsAssignableFrom<StatusCodeException>(new ResponseException(new Forbidden()));
    }

    [Fact]
    public void Message_NamesTheStatusWhenNoneWasGiven() {
        Assert.Contains("410", new ResponseException(new Gone()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Message_IsTheOneGivenWhenThereIsOne() {
        var exception = new ResponseException(new Gone(), "The board was deleted in March.");

        Assert.Equal("The board was deleted in March.", exception.Message);
    }

    [Fact]
    public void Constructor_RejectsANullResponse() {
        Assert.Throws<ArgumentNullException>(() => new ResponseException(null!));
    }
}
