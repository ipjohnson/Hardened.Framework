using Amazon.CDK.AWS.Apigatewayv2;
using Amazon.CDK.AWS.Lambda;

namespace Hardened.Amz.Cdk.Tests;

/// <summary>
/// A resource reference is how one stack names something another stack deployed. It is the key in
/// the deployment context's dictionary and the element compared when the deploy command works out
/// which stack produces what, so its equality is not a detail — it is the whole mechanism.
/// </summary>
public class CdkResourceRefTests {

    [Fact]
    public void TwoReferencesToTheSameNameAndTypeAreTheSameResource() {
        Assert.Equal(new CdkResourceRef<string>("orders"), new CdkResourceRef<string>("orders"));
    }

    [Fact]
    public void TwoReferencesToTheSameTypeUnderDifferentNamesAreDifferentResources() {
        Assert.NotEqual(new CdkResourceRef<string>("orders"), new CdkResourceRef<string>("billing"));
    }

    /// <summary>
    /// Both default to the name "default", so without the type in the comparison every unnamed
    /// reference in a deployment would be the same key — the shape <see cref="KnownCdkResources"/>
    /// is entirely built from.
    /// </summary>
    [Fact]
    public void TheSameNameUnderDifferentTypesIsADifferentResource() {
        Assert.NotEqual<object>(new CdkResourceRef<Function>(), new CdkResourceRef<Alias>());
    }

    [Fact]
    public void AReferenceWithNoNameIsCalledDefault() {
        Assert.Equal("default", new CdkResourceRef<Function>().Name);
    }

    [Fact]
    public void AReferenceReportsTheTypeOfResourceItNames() {
        ICdkResourceRef resource = new CdkResourceRef<Function>("api");

        Assert.Equal(typeof(Function), resource.TypeOfResource);
        Assert.Equal("api", resource.Name);
    }

    /// <summary>
    /// The well-known references are what a stack definition writes in its <c>Produces</c> and
    /// another writes in its <c>Consumes</c>, so they have to name three distinct resources rather
    /// than three references that happen to compare equal.
    /// </summary>
    [Fact]
    public void TheWellKnownResourcesAreThreeDistinctReferences() {
        var known = new ICdkResourceRef[] {
            KnownCdkResources.LambdaFunction,
            KnownCdkResources.LambdaFunctionAlias,
            KnownCdkResources.HttpApi,
        };

        Assert.Equal(3, known.Distinct().Count());
    }

    [Fact]
    public void TheWellKnownResourcesNameTheirCdkConstructTypes() {
        Assert.Equal(typeof(Function), ((ICdkResourceRef)KnownCdkResources.LambdaFunction).TypeOfResource);
        Assert.Equal(typeof(Alias), ((ICdkResourceRef)KnownCdkResources.LambdaFunctionAlias).TypeOfResource);
        Assert.Equal(typeof(HttpApi), ((ICdkResourceRef)KnownCdkResources.HttpApi).TypeOfResource);
    }

    /// <summary>
    /// A stack definition names a well-known resource by writing
    /// <c>KnownCdkResources.LambdaFunction</c>; the deploy command compares it against whatever the
    /// other definition wrote. Those are two reads of a static field, so they only match because the
    /// value is compared rather than the reference — which is what <c>Contains</c> in
    /// <c>SortStackDefinitions</c> depends on.
    /// </summary>
    [Fact]
    public void AWellKnownResourceMatchesAFreshReferenceToTheSameThing() {
        IEnumerable<ICdkResourceRef> produces = [KnownCdkResources.LambdaFunctionAlias];

        Assert.Contains(new CdkResourceRef<Alias>(), produces);
    }
}
