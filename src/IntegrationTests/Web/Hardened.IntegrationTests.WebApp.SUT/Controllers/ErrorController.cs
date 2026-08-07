using Hardened.Requests.Runtime.Errors;
using Hardened.Web.Runtime.Attributes;

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

    public class TenantMismatchException : BadRequestException {
        public TenantMismatchException() : base("tenant does not match") { }
    }

    public class BadgeNotFoundException : Exception {
        public BadgeNotFoundException() : base("no badge with that id") { }
    }
}
