using Hardened.Azure.Functions.Testing;
using Hardened.IntegrationTests.AzureStream.SUT.Generated;
using Xunit;

namespace Hardened.IntegrationTests.AzureStream.SUT.Tests;

/// <summary>
/// The provider the generator wrote and the file the Worker SDK's build task wrote describe the
/// same Event Hubs function; see <see cref="MetadataAgreement"/>.
/// </summary>
public class MetadataAgreementTests {
    [Fact]
    public async Task TheProviderAndTheBuildTaskAgree() {
        Assert.Empty(await MetadataAgreement.Disagreements(new AzureStreamTestAppAzureFunctionMetadataProvider()));
    }
}
