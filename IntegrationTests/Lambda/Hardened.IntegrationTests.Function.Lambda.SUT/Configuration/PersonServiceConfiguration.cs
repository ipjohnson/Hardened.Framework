using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Shared.Runtime.Attributes;

namespace Hardened.IntegrationTests.Function.Lambda.SUT.Configuration
{
    [ConfigurationModel]
    public partial class PersonServiceConfiguration2
    {
        private string _firstNamePrefix = "";
        private string _lastNamePrefix = "";
        private int? _ageBase;
    }
}
