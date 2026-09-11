using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Runtime.Errors;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Responses;

namespace Hardened.IntegrationTests.WebApp.SUT.Controllers;

/// <summary>
/// Handlers that throw, so exception-to-status mapping can be exercised through the real
/// pipeline rather than by calling the converter directly.
/// </summary>
[BasePath("/errors")]
public class ErrorController {

    /// <summary>An exception deriving from BadRequestException is a client error.</summary>
    [Get("/bad-request")]
    public string BadRequest() =>
        throw new BadRequestException("the request was malformed");

    /// <summary>A consumer-defined client error, identified by its base type.</summary>
    [Get("/derived-client-error")]
    public string DerivedClientError() =>
        throw new TenantMismatchException();

    [Get("/format")]
    public string Format() =>
        throw new FormatException("'abc' is not a number");

    /// <summary>Anything else is a server error.</summary>
    [Get("/server")]
    public string Server() =>
        throw new InvalidOperationException("the widget was not ready");

    /// <summary>
    /// Named so that the old substring classification would have called it a client error.
    /// It derives from Exception, so it must be a 500.
    /// </summary>
    [Get("/badge-missing")]
    public string BadgeMissing() =>
        throw new BadgeNotFoundException();

    /// <summary>
    /// Unsupported Content-Encoding is raised by the deserializers and is a client error.
    /// </summary>
    [Get("/bad-encoding")]
    public string BadEncoding() =>
        throw new BadContentEncodingException("deflate");

    /// <summary>
    /// A status the pipeline could not produce at all before <c>StatusCodeException</c>: every
    /// exception was either a 400 or a 500.
    /// </summary>
    [Get("/declared-status")]
    public string DeclaredStatus() =>
        throw new Hardened.Requests.Abstract.Errors.StatusCodeException(404);

    /// <summary>The same, carrying the body a specification declared for it.</summary>
    [Get("/declared-body")]
    public string DeclaredBody() =>
        throw new Hardened.Requests.Abstract.Errors.StatusCodeException(
            409, new ConflictBody("locked", "held by another writer"));

    /// <summary>
    /// A declared content type plus a thrown declared status. A single declaration on a handler
    /// returning text commits before the handler runs, so it is on the response when the error is
    /// serialized - and the raw writer takes only strings, bytes and streams, never an error model.
    /// </summary>
    [Produces("text/plain")]
    [Get("/raw-declared-status")]
    public string RawDeclaredStatus() =>
        throw new Hardened.Requests.Abstract.Errors.StatusCodeException(
            409, new ConflictBody("locked", "held by another writer"));

    /// <summary>The same commitment against an unclassified fault.</summary>
    [Produces("image/png")]
    [Get("/raw-server-error")]
    public byte[] RawServerError() =>
        throw new InvalidOperationException("the image was not ready");

    /// <summary>
    /// A handler that succeeds and a response that cannot be written.
    /// </summary>
    /// <remarks>
    /// The one failure <c>IoFilter</c> does not record. It catches parameter binding and it
    /// catches the handler; the response writer runs after both, so a payload the serializer
    /// refuses throws past every filter. Over a socket the host answers 500. Through the pipeline
    /// host it used to unwind out of <c>app.Get</c> and fail the test with the raw exception,
    /// which is the divergence a harness exists not to have.
    /// </remarks>
    [Get("/unwritable")]
    public UnwritableBody Unwritable() => new();

    public record ConflictBody(string Code, string Message);

    /// <summary>A payload whose getter throws, so writing it fails and running it does not.</summary>
    public class UnwritableBody {
        public string Value => throw new NotSupportedException("this value cannot be written");
    }

    public class TenantMismatchException : BadRequestException {
        public TenantMismatchException() : base("tenant does not match") { }
    }

    public class BadgeNotFoundException : Exception {
        public BadgeNotFoundException() : base("no badge with that id") { }
    }
}
