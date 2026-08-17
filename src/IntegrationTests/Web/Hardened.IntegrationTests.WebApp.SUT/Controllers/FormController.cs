using Hardened.Web.Runtime.Attributes;

namespace Hardened.IntegrationTests.WebApp.SUT.Controllers;

/// <summary>
/// Handlers binding from an <c>application/x-www-form-urlencoded</c> body.
/// </summary>
/// <remarks>
/// The wire format is a query string in the body, so what is worth covering here is the places the
/// two differ or where the body being a stream matters - the <c>+</c> that means a space, a field
/// sent more than once, and a request that sends no form at all.
/// </remarks>
[BasePath("/form")]
public class FormController {

    /// <summary>Two fields, the ordinary case.</summary>
    [Post("/sign-in")]
    public string SignIn([FromForm] string username, [FromForm] string password) =>
        username + ":" + password;

    /// <summary>A field converted to something that is not a string.</summary>
    [Post("/quantity")]
    public int Quantity([FromForm] int count) => count * 2;

    /// <summary>A renamed field, so the parameter and the wire name can differ.</summary>
    [Post("/renamed")]
    public string Renamed([FromForm("user_name")] string userName) => userName;

    /// <summary>
    /// An absent field with a default, and an optional one without.
    /// </summary>
    /// <remarks>
    /// The same conversion path every other string-valued source uses, so a missing form field
    /// behaves like a missing query parameter rather than throwing.
    /// </remarks>
    [Post("/optional")]
    public string Optional([FromForm] string present, [FromForm] string missing = "fallback") =>
        present + ":" + missing;
}
