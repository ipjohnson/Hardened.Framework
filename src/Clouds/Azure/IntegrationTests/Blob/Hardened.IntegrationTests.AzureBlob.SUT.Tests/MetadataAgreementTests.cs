using Hardened.Azure.Functions.Testing;
using Hardened.IntegrationTests.AzureBlob.SUT.Generated;
using Xunit;

namespace Hardened.IntegrationTests.AzureBlob.SUT.Tests;

/// <summary>
/// The provider the generator wrote and the file the Worker SDK's build task wrote describe the
/// same blob function; see <see cref="MetadataAgreement"/>.
/// </summary>
public class MetadataAgreementTests {
    [Fact]
    public async Task TheProviderAndTheBuildTaskAgree() {
        Assert.Empty(await MetadataAgreement.Disagreements(new AzureBlobTestAppAzureFunctionMetadataProvider()));
    }
}
