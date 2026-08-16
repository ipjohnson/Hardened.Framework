using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Web.Runtime.Attributes;

namespace Hardened.IntegrationTests.WebApp.SUT.Controllers;

/// <summary>
/// Setting a cookie on the way out, which nothing in this suite did until the harness stopped
/// dropping them.
/// </summary>
[BasePath("/cookies")]
public class ResponseCookieController {

    [Get("/set")]
    public string SetCookie(IExecutionContext context) {
        context.Response.Cookies.Append("session", "abc123");

        return "set";
    }

    [Get("/set-with-options")]
    public string SetCookieWithOptions(IExecutionContext context) {
        context.Response.Cookies.Append(
            "preference", "dark",
            new CookieSetOptions(Path: "/app", HttpOnly: true, Secure: true));

        return "set";
    }
}
