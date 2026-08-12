namespace Hardened.Amz.Function.Lambda.Runtime.Tests;

/// <summary>
/// <c>[FromContext]</c> is read by the Lambda function source generator to bind a parameter out of
/// the invocation context. An unnamed use binds by parameter name, which is why <c>Name</c> has to
/// stay null rather than defaulting to a string.
/// </summary>
public class FromContextAttributeTests {

    [Fact]
    public void AnUnnamedBindingCarriesNoName() {
        Assert.Null(new FromContextAttribute().Name);
    }

    [Fact]
    public void ANamedBindingCarriesTheNameItWasGiven() {
        Assert.Equal("awsRequestId", new FromContextAttribute("awsRequestId").Name);
    }
}
