using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Shared.Runtime.Attributes;

namespace Hardened.IntegrationTests.Web.SUT
{
    public interface ISomeTestService
    {

    }

    [Expose]
    internal class SomeTestService : ISomeTestService
    {
    }
}
