using Amazon.Lambda.Core;

namespace Hardened.Amz.Shared.Lambda.Testing;

public class TestCognitoIdentity : ICognitoIdentity
{
    public TestCognitoIdentity() : this("TestIdentity", "TestPool")
    {

    }

    public TestCognitoIdentity(string identityId, string identityPoolId)
    {
        IdentityId = identityId;
        IdentityPoolId = identityPoolId;
    }

    public string IdentityId { get; }

    public string IdentityPoolId { get; }
}