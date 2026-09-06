using Hardened.Amz.Cdk.Commands;

namespace Hardened.Amz.Cdk.Tests;

/// <summary>
/// Which AWS account a stack deploys into. A deployment reaching more than one account describes
/// them all on one configuration object, and each stack definition names the one it wants through
/// <c>AccountType</c> — so the lookup is by property name, resolved at deploy time.
/// </summary>
public class DeploymentAccountProviderTests {

    [Fact]
    public void TheAccountIsThePropertyNamedByTheStacksAccountType() {
        Assert.Equal("111122223333", Account(new MultiAccount(), "ServiceAccount"));
    }

    [Fact]
    public void ADeploymentReachingTwoAccountsPicksThemApartByName() {
        var configuration = new MultiAccount();

        Assert.Equal("111122223333", Account(configuration, "ServiceAccount"));
        Assert.Equal("444455556666", Account(configuration, "NetworkAccount"));
    }

    /// <summary>
    /// Naming an account type nothing describes is a typo in a stack definition, and it is worth
    /// finding out at synth rather than at deploy. The message repeats the name asked for.
    /// </summary>
    [Fact]
    public void AnAccountTypeTheConfigurationDoesNotDescribeIsRejectedByName() {
        var error = Assert.Throws<ApplicationException>(() => Account(new MultiAccount(), "AuditAccount"));

        Assert.Contains("AuditAccount", error.Message);
    }

    /// <summary>
    /// An account id is a string. A property of the right name holding anything else is not the
    /// account, and treating it as one would put whatever it stringified to into the stack's
    /// environment.
    /// </summary>
    [Fact]
    public void APropertyOfTheRightNameThatIsNotAStringIsNotAnAccount() {
        Assert.Throws<ApplicationException>(() => Account(new NumericAccount(), "ServiceAccount"));
    }

    /// <summary>
    /// A configuration is per deployment; a static property belongs to the type, so it cannot be
    /// describing this deployment's account.
    /// </summary>
    [Fact]
    public void AStaticPropertyOfTheRightNameIsNotAnAccount() {
        Assert.Throws<ApplicationException>(() => Account(new StaticAccount(), "ServiceAccount"));
    }

    /// <summary>
    /// An account left unset is the same as one never described — it is what a configuration that
    /// only fills in the accounts a stage actually uses looks like.
    /// </summary>
    [Fact]
    public void AnAccountLeftUnsetIsTreatedAsNotDescribed() {
        Assert.Throws<ApplicationException>(() => Account(new UnsetAccount(), "ServiceAccount"));
    }

    private static string Account(object configuration, string accountType) =>
        new DeploymentAccountProvider().GetDeploymentAccount(configuration, accountType);

    private sealed class MultiAccount {
        public string ServiceAccount => "111122223333";

        public string NetworkAccount => "444455556666";
    }

    private sealed class NumericAccount {
        public long ServiceAccount => 111122223333L;
    }

    private sealed class StaticAccount {
        public static string ServiceAccount => "111122223333";
    }

    private sealed class UnsetAccount {
        public string? ServiceAccount => null;
    }
}
