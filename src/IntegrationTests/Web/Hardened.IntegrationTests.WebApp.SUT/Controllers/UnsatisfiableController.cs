using Hardened.Web.Runtime.Attributes;

namespace Hardened.IntegrationTests.WebApp.SUT.Controllers;

/// <summary>
/// A handler whose constructor asks for a service nothing registers.
/// </summary>
/// <remarks>
/// H-13 from the 0.19.0-rc1000 trial. The handler is constructed at
/// <c>FilterOrder.HandlerCreation</c>, ahead of the filter that writes a response, and the
/// container's exception unwound past it, so this answered a 500 with <c>Content-Length: 0</c>.
/// </remarks>
public class UnsatisfiableController {

    public UnsatisfiableController(IUnregisteredService service) {
        _ = service;
    }

    [Get("/errors/unsatisfiable")]
    public string Get() => "unreachable";
}

public interface IUnregisteredService { }
