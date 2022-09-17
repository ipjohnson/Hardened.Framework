using Hardened.Shared.Testing.Impl;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Sdk;

namespace Hardened.Shared.Testing.Attributes
{
    [XunitTestCaseDiscoverer("Hardened.Shared.Testing.Impl." + nameof(HardenedTestDiscoverer), "Hardened.Shared.Testing")]
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class HardenedTestAttribute : FactAttribute
    {

    }
}
