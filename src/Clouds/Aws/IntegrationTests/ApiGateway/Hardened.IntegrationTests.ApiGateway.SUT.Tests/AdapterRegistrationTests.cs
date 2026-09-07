using Hardened.Aws.Lambda.Runtime.Adapters;
using Hardened.Requests.Abstract.Execution;
using Hardened.Shared.Testing.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.IntegrationTests.ApiGateway.SUT.Tests;

/// <summary>
/// What the verbs on the controller registered.
///
/// <para>
/// The one thing in this suite that is deliberately about AWS. Everything else is written as a web
/// test and would pass on any host; these two say that on this host the verbs bound the API Gateway
/// adapter and that it answers a failure rather than rethrowing - which is the difference between
/// the HTTP family and every other one.
/// </para>
/// </summary>
public class AdapterRegistrationTests {

    /// <summary>
    /// Verbs bound the adapter. Nothing in the application mentions API Gateway, SQS or Lambda.
    /// </summary>
    [HardenedTest]
    public void TheVerbsRegisteredTheGatewayAdapter(IServiceProvider provider) {
        Assert.IsType<ApiGatewayAdapter>(Assert.Single(provider.GetServices<IPayloadAdapter>()));
    }

    /// <summary>
    /// A web application answers rather than failing. The caller is on the other end of a
    /// connection, and a failed invocation would give them a 502 with nothing in it.
    /// </summary>
    [HardenedTest]
    public void TheGatewayAdapterAnswersFailuresRatherThanRethrowing(IServiceProvider provider) {
        var adapter = Assert.Single(provider.GetServices<IPayloadAdapter>());

        Assert.Equal(HostFailurePolicy.Answer500, adapter.FailurePolicy);
    }
}
