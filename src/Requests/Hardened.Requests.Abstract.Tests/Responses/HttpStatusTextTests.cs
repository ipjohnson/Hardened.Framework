using Hardened.Requests.Abstract.Responses;
using Xunit;

namespace Hardened.Requests.Abstract.Tests.Responses;

/// <summary>
/// The reason phrases RFC 9110 gives. A problem document's <c>title</c> and an ALB answer's
/// <c>statusDescription</c> are both read from this table, so each entry is on the wire.
/// </summary>
public class HttpStatusTextTests
{
    [Theory]
    [InlineData(100, "Continue")]
    [InlineData(101, "Switching Protocols")]
    [InlineData(200, "OK")]
    [InlineData(201, "Created")]
    [InlineData(202, "Accepted")]
    [InlineData(203, "Non-Authoritative Information")]
    [InlineData(204, "No Content")]
    [InlineData(205, "Reset Content")]
    [InlineData(206, "Partial Content")]
    [InlineData(300, "Multiple Choices")]
    [InlineData(301, "Moved Permanently")]
    [InlineData(302, "Found")]
    [InlineData(303, "See Other")]
    [InlineData(304, "Not Modified")]
    [InlineData(307, "Temporary Redirect")]
    [InlineData(308, "Permanent Redirect")]
    [InlineData(400, "Bad Request")]
    [InlineData(401, "Unauthorized")]
    [InlineData(402, "Payment Required")]
    [InlineData(403, "Forbidden")]
    [InlineData(404, "Not Found")]
    [InlineData(405, "Method Not Allowed")]
    [InlineData(406, "Not Acceptable")]
    [InlineData(407, "Proxy Authentication Required")]
    [InlineData(408, "Request Timeout")]
    [InlineData(409, "Conflict")]
    [InlineData(410, "Gone")]
    [InlineData(411, "Length Required")]
    [InlineData(412, "Precondition Failed")]
    [InlineData(413, "Content Too Large")]
    [InlineData(414, "URI Too Long")]
    [InlineData(415, "Unsupported Media Type")]
    [InlineData(416, "Range Not Satisfiable")]
    [InlineData(417, "Expectation Failed")]
    [InlineData(421, "Misdirected Request")]
    [InlineData(422, "Unprocessable Content")]
    [InlineData(426, "Upgrade Required")]
    [InlineData(428, "Precondition Required")]
    [InlineData(429, "Too Many Requests")]
    [InlineData(431, "Request Header Fields Too Large")]
    [InlineData(451, "Unavailable For Legal Reasons")]
    [InlineData(500, "Internal Server Error")]
    [InlineData(501, "Not Implemented")]
    [InlineData(502, "Bad Gateway")]
    [InlineData(503, "Service Unavailable")]
    [InlineData(504, "Gateway Timeout")]
    [InlineData(505, "HTTP Version Not Supported")]
    public void EachStatusHasItsReasonPhrase(int status, string phrase)
    {
        Assert.Equal(phrase, HttpStatusText.ReasonPhrase(status));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(299)]
    [InlineData(418)]
    [InlineData(499)]
    [InlineData(599)]
    public void AStatusRfc9110DoesNotNameHasNone(int status)
    {
        Assert.Null(HttpStatusText.ReasonPhrase(status));
    }
}
