using Hardened.Azure.Functions.Http;
using Hardened.Azure.Functions.Runtime.Adapters;
using Hardened.IntegrationTests.AzureHttp.SUT.Generated;
using Hardened.Azure.Functions.Testing;
using Hardened.Requests.Abstract.Execution;
using Hardened.Shared.Testing.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.IntegrationTests.AzureHttp.SUT.Tests;

/// <summary>
/// What the verbs on the controller registered.
///
/// <para>
/// The one thing in this suite that is deliberately about Azure. Everything else is written as a
/// web test and would pass on any host; these say that on this host the verbs bound the HTTP
/// adapter, that it answers a failure rather than rethrowing - which is the difference between the
/// HTTP family and every other one - and that the one function the generator wrote is the one the
/// build task found.
/// </para>
/// </summary>
public class AdapterRegistrationTests {

    /// <summary>
    /// Verbs bound the adapter. Nothing in the application mentions the HTTP trigger or Azure.
    /// </summary>
    [HardenedTest]
    public void TheVerbsRegisteredTheHttpAdapter(IServiceProvider provider) {
        Assert.IsType<HttpAdapter>(Assert.Single(provider.GetServices<ITriggerAdapter>()));
    }

    /// <summary>
    /// A web application answers rather than failing. The caller is on the other end of a
    /// connection, and a failed invocation would give them the host's 500 with nothing the
    /// application chose in it.
    /// </summary>
    [HardenedTest]
    public void TheHttpAdapterAnswersFailuresRatherThanRethrowing(IServiceProvider provider) {
        var adapter = Assert.Single(provider.GetServices<ITriggerAdapter>());

        Assert.Equal(HostFailurePolicy.Answer500, adapter.FailurePolicy);
    }

    /// <summary>
    /// One function, catching every method under every path, and the provider and the build task
    /// describe it the same way.
    /// </summary>
    [Fact]
    public async Task TheProviderAndTheBuildTaskAgree() {
        Assert.Empty(await MetadataAgreement.Disagreements(new AzureHttpTestAppAzureFunctionMetadataProvider()));
    }
}
