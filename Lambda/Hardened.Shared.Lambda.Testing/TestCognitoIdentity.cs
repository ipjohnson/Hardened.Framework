using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Amazon.Lambda.Core;

namespace Hardened.Shared.Lambda.Testing
{
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
}
