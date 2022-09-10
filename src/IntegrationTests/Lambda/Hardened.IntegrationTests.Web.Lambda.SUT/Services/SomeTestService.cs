using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Shared.Runtime.Attributes;

namespace Hardened.IntegrationTests.Web.Lambda.SUT.Services
{
    public interface ISomeTestService
    {
        string TestMethod();
    }

    [Expose]
    internal class SomeTestService : ISomeTestService
    {
        public string TestMethod()
        {
            return "test string";
        }
    }
}
