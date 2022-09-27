using Hardened.Shared.Runtime.Attributes;

namespace Hardened.IntegrationTests.Web.Lambda.SUT.Configuration
{
    [ConfigurationModel]
    public partial class PersonServiceConfiguration
    {
        [FromEnvironmentVariable("FIRST_NAME")]
        private string _firstNamePrefix = "";
        private string _lastNamePrefix = "";
        private int? _ageBase;
        private bool _locked;
    }
}
