using Hardened.Azure.Functions.Testing;
using Hardened.IntegrationTests.AzureQueue.Settlement.SUT.Generated;
using Xunit;

namespace Hardened.IntegrationTests.AzureQueue.Settlement.SUT.Tests;

/// <summary>
/// The provider the generator wrote and the file the Worker SDK's build task wrote describe the
/// same queue function, and both say the host is not to complete on its behalf.
/// </summary>
public class MetadataAgreementTests {
    [Fact]
    public async Task TheProviderAndTheBuildTaskAgree() {
        Assert.Empty(await MetadataAgreement.Disagreements(new SettlementTestAppAzureFunctionMetadataProvider()));
    }

    /// <summary>
    /// The one line on the application reached the binding: without it the host would complete
    /// every message the function abandoned.
    /// </summary>
    [Fact]
    public async Task TheBindingTurnsOffTheHostsCompletion() {
        var function = Assert.Single(
            await new SettlementTestAppAzureFunctionMetadataProvider().GetFunctionMetadataAsync(AppContext.BaseDirectory));

        var trigger = Assert.Single(function.RawBindings!, binding => binding.Contains("serviceBusTrigger"));

        Assert.Contains("\"autoCompleteMessages\":false", trigger);
    }
}
