using Hardened.IntegrationTests.OpenApi.SUT.Services;
using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Runtime.Authorization;

namespace Hardened.IntegrationTests.OpenApi.SUT;

/// <summary>
/// A handler guarded by an attribute the implementation wrote, on an operation the description
/// declares public.
/// </summary>
/// <remarks>
/// <para>
/// The opposite direction from <see cref="SecuredServiceImpl"/>, and the one nothing was driving.
/// <c>described-authorization.md</c> states that a described requirement arrives as one more entry
/// in the handler's metadata alongside anything the implementation declared, so a contract may
/// narrow a route and can never widen one - <c>security: []</c> does not strip an
/// <c>[AuthorizeGrants]</c> somebody wrote here.
/// </para>
/// <para>
/// That guarantee was reported as not holding in contract-first mode, on the grounds that no path
/// existed from a C# attribute into the generated metadata. The path exists:
/// <c>HandlerSelector</c> collects every attribute on a <c>[Handler]</c> class,
/// <c>RequestModelBuilder.EnrichWithHandlerFilters</c> appends them to the model the description
/// produced, and <c>IExecutionRequestHandlerInfo.RequirementFrom</c> conjoins every
/// <c>IAuthorizeAttribute</c> it finds. What was missing was a test, which is why the claim was
/// credible.
/// </para>
/// </remarks>
[Handler]
[AuthorizeGrants("guarded:enter")]
public class GuardedServiceImpl : IGuardedService {
    public Task<string> GuardedByAttribute() => Task.FromResult("reached");
}
