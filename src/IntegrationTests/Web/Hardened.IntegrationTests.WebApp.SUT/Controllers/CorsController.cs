using Hardened.IntegrationTests.WebApp.SUT.Models;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Cors;

namespace Hardened.IntegrationTests.WebApp.SUT.Controllers;

/// <summary>
/// The one controller that declares CORS. Its declaration moves this application to CORS per route,
/// so every other route answers a cross-origin request with no CORS headers.
/// </summary>
[Cors]
[BasePath("/cors")]
public class CorsController
{
    [Get("/greeting")]
    public string Greeting() => "hello";

    /// <summary>
    /// The registration model's constraints, so a body that breaks them is refused with 400 after
    /// the CORS filter has run.
    /// </summary>
    [Post("/sign-up")]
    public string SignUp(RegistrationModel model) => model.Name ?? "";
}
