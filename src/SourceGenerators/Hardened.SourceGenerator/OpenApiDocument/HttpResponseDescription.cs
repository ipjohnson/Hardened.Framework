namespace Hardened.SourceGenerator.OpenApiDocument;

/// <summary>
/// The <c>description</c> a response object carries, which OpenAPI requires of every one of them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not shared with the tables that name types, and that is deliberate.</b> There is one of
/// those - <c>ShippedResponses.StatusName</c>, which the specification-first path composes
/// <c>NotFoundProblem</c> and <c>ConflictProblem</c> from - and it holds some of the same strings
/// while answering a different question. Teaching it the 2xx phrases a document needs would rename
/// generated types for a reason that has nothing to do with them, and giving this one a type name
/// would put <c>ContentTooLarge</c> in a document where RFC 9110 writes "Content Too Large". A
/// table that names types and a table that describes responses are one refactor apart from being
/// the same thing and must not be made so.
/// </para>
/// <para>
/// The phrases are RFC 9110's own, because a description is read by whoever reads the document and
/// the status's registered name is the one thing they already know it by.
/// </para>
/// </remarks>
public static class HttpResponseDescription {

    /// <summary>
    /// The phrase for <paramref name="status"/>, or a generic one for a status not listed.
    /// </summary>
    /// <remarks>
    /// The fallback names the status rather than saying "Error", so a document declaring an
    /// unusual code still tells a reader which one - and never claims a 2xx is a failure, which
    /// naming everything unlisted "Error" would.
    /// </remarks>
    public static string For(int status) {
        switch (status) {
            case 200: return "OK";
            case 201: return "Created";
            case 202: return "Accepted";
            case 204: return "No Content";
            case 206: return "Partial Content";
            case 301: return "Moved Permanently";
            case 302: return "Found";
            case 303: return "See Other";
            case 304: return "Not Modified";
            case 307: return "Temporary Redirect";
            case 308: return "Permanent Redirect";
            case 400: return "Bad Request";
            case 401: return "Unauthorized";
            case 402: return "Payment Required";
            case 403: return "Forbidden";
            case 404: return "Not Found";
            case 405: return "Method Not Allowed";
            case 406: return "Not Acceptable";
            case 408: return "Request Timeout";
            case 409: return "Conflict";
            case 410: return "Gone";
            case 412: return "Precondition Failed";
            case 413: return "Content Too Large";
            case 415: return "Unsupported Media Type";
            case 422: return "Unprocessable Content";
            case 428: return "Precondition Required";
            case 429: return "Too Many Requests";
            case 500: return "Internal Server Error";
            case 501: return "Not Implemented";
            case 502: return "Bad Gateway";
            case 503: return "Service Unavailable";
            case 504: return "Gateway Timeout";
            default: return "Status " + status.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
