using Hardened.Shared.Runtime.Attributes;

namespace Hardened.Amz.CloudWatch.Dashboards.Configuration;

[ConfigurationModel]
public partial class CssConfig
{
    private string _primary = "#0073bb";
    private string _secondary = "#6c757d";
    private string _danger = "#dc3545";
    private string _action = "#EC7211";
}