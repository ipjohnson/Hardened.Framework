using Hardened.Azure.Functions.Testing;
using Hardened.IntegrationTests.AzureChange.SUT.Generated;
using Xunit;

namespace Hardened.IntegrationTests.AzureChange.SUT.Tests;

/// <summary>
/// The provider the generator wrote and the file the Worker SDK's build task wrote describe the
/// same two change feed functions, database and all; see <see cref="MetadataAgreement"/>.
/// </summary>
public class MetadataAgreementTests {
    [Fact]
    public async Task TheProviderAndTheBuildTaskAgree() {
        Assert.Empty(await MetadataAgreement.Disagreements(new AzureChangeTestAppAzureFunctionMetadataProvider()));
    }
}
