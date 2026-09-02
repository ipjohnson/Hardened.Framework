using Hardened.IntegrationTests.OpenApi.SUT.Services;
using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Errors;

namespace Hardened.IntegrationTests.OpenApi.SUT;

/// <summary>
/// Handlers that exist to be refused.
/// </summary>
/// <remarks>
/// <para>
/// None carries an authorization attribute. Everything guarding them came from the description,
/// which is the point: reaching one of these methods at all is the failure these routes test for.
/// </para>
/// <para>
/// <c>SecuredOwned</c> is the other half. A contract can say the caller must hold a grant and
/// cannot say the row must be theirs, so the ownership check belongs to the handler - which needs
/// to know who the caller is. A specification-first handler implements a generated interface, so
/// it cannot take <c>IExecutionContext</c> as a parameter; <see cref="ICurrentCaller"/> is what it
/// takes instead, by plain constructor injection with nothing wired by hand.
/// </para>
/// </remarks>
[Handler]
public class SecuredServiceImpl(ICurrentCaller caller) : ISecuredService {
    public Task<string> SecuredScoped() => Task.FromResult("reached");

    public Task<string> SecuredEither() => Task.FromResult("reached");

    public Task<string> SecuredOwned(string ownerId) =>
        ownerId == caller.Principal.Subject
            ? Task.FromResult(ownerId)
            : throw new StatusCodeException(403);
}
