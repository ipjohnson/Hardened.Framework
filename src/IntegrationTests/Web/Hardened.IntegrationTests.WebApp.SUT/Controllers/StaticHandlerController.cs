using Hardened.IntegrationTests.WebApp.SUT.Models;
using Hardened.IntegrationTests.WebApp.SUT.Services;
using Hardened.Web.Runtime.Attributes;

namespace Hardened.IntegrationTests.WebApp.SUT.Controllers;

/// <summary>
/// Handlers with no instance to construct, served through the whole pipeline.
/// </summary>
/// <remarks>
/// A static class, which is the half of this the generator tests cannot reach on their own: nothing
/// registers it, nothing resolves it, and the container the application actually builds has to be
/// able to serve these anyway.
/// </remarks>
[BasePath("/static")]
public static class StaticHandlerController
{
    [Get("/echo/{value}")]
    public static string Echo(string value) => value;

    /// <summary>
    /// Dependencies as parameters, which is the whole of what a static handler gives up the
    /// constructor for.
    /// </summary>
    [Post("/add")]
    public static int Add(IMathService<int> mathService, MathAddModel model) =>
        mathService.Add(model.Values?.ToArray() ?? Array.Empty<int>());

    [Get("/async/{value}")]
    public static Task<string> Async(string value) => Task.FromResult(value);
}

/// <summary>
/// One controller holding both kinds, which is what keeps its registration alive.
/// </summary>
[BasePath("/mixed")]
public class MixedHandlerController
{
    private readonly IMathService<int> _mathService;

    public MixedHandlerController(IMathService<int> mathService)
    {
        _mathService = mathService;
    }

    [Get("/static/{value}")]
    public static string Stat(string value) => value;

    [Post("/instance")]
    public int Instance(MathAddModel model) =>
        _mathService.Add(model.Values?.ToArray() ?? Array.Empty<int>());
}
