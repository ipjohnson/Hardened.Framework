using Hardened.Aws.Lambda.Sqs;
using Xunit;

namespace Hardened.IntegrationTests.Sqs.SUT.Tests;

/// <summary>
/// Two declarations of the module are one install whatever <c>ReportBatchItemFailures</c> says,
/// so one function never loads two SQS adapters.
/// </summary>
public class SqsModuleTests
{
    [Fact]
    public void EveryInstallOfTheModuleIsTheSameInstall()
    {
        var reporting = new SqsModule { ReportBatchItemFailures = true };

        Assert.Equal(new SqsModule(), reporting);
        Assert.Equal(new SqsModule().GetHashCode(), reporting.GetHashCode());
    }
}
