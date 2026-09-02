using Hardened.IntegrationTests.OpenApi.SUT.Models;
using Hardened.IntegrationTests.OpenApi.SUT.Services;
using Hardened.Requests.Abstract.Attributes;

namespace Hardened.IntegrationTests.OpenApi.SUT;

/// <summary>
/// Echoes the order back, so a test can see what the framework decided the body was.
/// </summary>
/// <remarks>
/// Deliberately does no checking of its own. The point of the route is that a request reaching this
/// method at all is a failure of validation - both defects it covers ended with the handler running
/// on a body the caller never sent. The idempotency key is taken and ignored for the same reason:
/// what is under test is the refusal it never reaches.
/// </remarks>
[Handler]
public class OrderServiceImpl : IOrderService {
    public Task<OrderRequest> PlaceOrder(string? idempotencyKey, OrderRequest body) =>
        Task.FromResult(body);
}
