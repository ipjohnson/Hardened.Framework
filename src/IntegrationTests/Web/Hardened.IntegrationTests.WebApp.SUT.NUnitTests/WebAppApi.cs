using Hardened.IntegrationTests.WebApp.SUT.Models;
using Refit;

namespace Hardened.IntegrationTests.WebApp.SUT.NUnitTests;

/// <summary>Two of the operations the xUnit project's interface declares, enough to read an answer through Refit here.</summary>
public interface IWebAppApi {

    [Post("/verbs/located")]
    Task<IApiResponse<MathAddModel>> CreateLocated([Body] MathAddModel model);

    [Get("/authorization/pets")]
    Task<IApiResponse<string>> Pets();
}
