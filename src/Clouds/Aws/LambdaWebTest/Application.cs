using Hardened.Requests.Runtime.Configuration;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Shared.Runtime.Configuration;

namespace LambdaWebTest;

[HardenedModule]
public partial class Application {
    private void Configure(IAppConfig config) {
        config.Amend((ResponseHeaderConfiguration response) => response.Add("Access-Control-Allow-Origin", "*"));
    }
}