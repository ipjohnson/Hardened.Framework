namespace Hardened.IntegrationTests.OpenApi.SUT.Tests;

/// <summary>
/// A filter typed on an interface <c>x-hardened-request-interfaces</c> names, reading a request.
/// </summary>
/// <remarks>
/// The generated text implementing the interface and a filter seeing it are different claims.
/// The filter tests <c>Parameters</c> against the interface at run time, so a base list that lost
/// the interface would compile and send no header.
/// </remarks>
public class RequestInterfaceTests
{
    [ModuleTest]
    public async Task AFilterTypedOnTheInterfaceReadsTheTenant(ITestWebApp testWebApp)
    {
        var response = await testWebApp.Get("/tenants/acme/orders");

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("acme", response.Headers[TenantHeaderAttribute.Header].ToString());
    }

    [ModuleTest]
    public async Task AnOperationThatClearsTheListIsNotATenantRequest(ITestWebApp testWebApp)
    {
        var response = await testWebApp.Get("/tenants/status");

        Assert.Equal(200, response.StatusCode);
        Assert.False(response.Headers.ContainsKey(TenantHeaderAttribute.Header));
    }
}
