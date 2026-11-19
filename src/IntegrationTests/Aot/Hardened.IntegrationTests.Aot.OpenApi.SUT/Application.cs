using System.Text.Json.Serialization;
using Hardened.IntegrationTests.Aot.OpenApi.SUT.Services;
using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Runtime;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Kestrel.Runtime;
using Hardened.Web.Runtime.DependencyInjection;

namespace Hardened.IntegrationTests.Aot.OpenApi.SUT;

[HardenedModule]
[HardenedWebModule]
[KestrelRuntime]
[AotSerializerModule]
public partial class Application;

[JsonSourceGenerationOptions(System.Text.Json.JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(List<string>))]
public partial class AotContext : JsonSerializerContext;

/// <summary>
/// The interface <c>tenants.yaml</c> names for every operation's request.
/// </summary>
/// <remarks>
/// The member is named as the generated property is, after the parameter.
/// </remarks>
public interface ITenantRequest
{
    string tenant { get; }
}

[Handler]
[TenantHeader]
public class TenantServiceImpl : ITenantService
{
    public Task<List<string>> ListTenantOrders(string tenant) =>
        Task.FromResult(new List<string> { "order-1" });

    public Task<string> TenantStatus(bool? verbose) => Task.FromResult("up");
}

/// <summary>
/// Writes the request's tenant to <c>X-Tenant</c>, read through <see cref="ITenantRequest"/>
/// rather than by parameter name.
/// </summary>
public sealed class TenantHeaderAttribute : Attribute, IRequestFilterProvider
{
    public const string Header = "X-Tenant";

    public IEnumerable<RequestFilterInfo> GetFilters(IExecutionRequestHandlerInfo handlerInfo)
    {
        var filter = new TenantHeaderFilter();

        yield return new RequestFilterInfo(
            _ => filter,
            FilterOrder.DefaultValue,
            nameof(TenantHeaderFilter)
        );
    }

    private sealed class TenantHeaderFilter : IExecutionFilter
    {
        public Task Execute(IExecutionChain chain)
        {
            var context = chain.Context;

            if (context.Request.Parameters is ITenantRequest request)
            {
                context.Response.Headers[Header] = request.tenant;
            }

            return chain.Next();
        }
    }
}
