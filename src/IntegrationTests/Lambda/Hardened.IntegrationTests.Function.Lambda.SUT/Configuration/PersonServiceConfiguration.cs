using Hardened.Shared.Runtime.Attributes;

namespace Hardened.IntegrationTests.Function.Lambda.SUT.Configuration;

[ConfigurationModel]
public partial class PersonServiceConfiguration
{
    private string _firstNamePrefix = "";
    private string _lastNamePrefix = "";
    private int? _ageBase;
}