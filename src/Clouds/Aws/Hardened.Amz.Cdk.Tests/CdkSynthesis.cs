namespace Hardened.Amz.Cdk.Tests;

/// <summary>
/// The collection every test class that synthesises a stack belongs to, so they run one at a time.
/// jsii unpacks its runtime and the CDK's asset tarballs into a shared temporary directory on first
/// use, and two classes starting it concurrently fail on each other's half-written files.
/// </summary>
[CollectionDefinition(Name)]
public class CdkSynthesis {
    public const string Name = "CDK synthesis";
}
