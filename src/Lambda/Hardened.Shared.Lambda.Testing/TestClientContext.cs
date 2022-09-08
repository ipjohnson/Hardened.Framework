using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Amazon.Lambda.Core;

namespace Hardened.Shared.Lambda.Testing
{
    internal class TestClientContext : IClientContext
    {
        public TestClientContext() 
        : this(new Dictionary<string, string>(), new TestClientApplication(), new Dictionary<string, string>())
        {

        }

        public TestClientContext(IDictionary<string, string> custom)
            : this(new Dictionary<string, string>(), new TestClientApplication(), custom)
        {

        }

        public TestClientContext(IDictionary<string, string> environment, IClientApplication client, IDictionary<string, string> custom)
        {
            Environment = environment;
            Client = client;
            Custom = custom;
        }

        public IDictionary<string, string> Environment { get; }

        public IClientApplication Client { get; }

        public IDictionary<string, string> Custom { get; }
    }
}
