using Hardened.IntegrationTests.OpenApi.SUT.Services;
using Hardened.Requests.Abstract.Attributes;

namespace Hardened.IntegrationTests.OpenApi.SUT;

/// <summary>
/// Handlers that exist to be refused.
/// </summary>
/// <remarks>
/// Neither carries an authorization attribute. Everything guarding them came from the description,
/// which is the point: reaching either method at all is the failure these routes test for.
/// </remarks>
[Handler]
public class SecuredServiceImpl : ISecuredService {
    public Task<string> SecuredScoped() => Task.FromResult("reached");

    public Task<string> SecuredEither() => Task.FromResult("reached");
}
