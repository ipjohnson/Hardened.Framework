using Hardened.Amz.Web.Lambda.Runtime.DependencyInjection;
using Hardened.Requests.Runtime.Configuration;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Shared.Runtime.Configuration;

namespace LambdaWebTest;

/// <summary>
/// A Lambda application served through API Gateway, buffered.
///
/// <para>
/// <c>[LambdaWebModule]</c> brings the API Gateway host and, through the
/// <c>[HardenedWebModule]</c> it carries, the web pipeline. It was missing until 2026-08-15, so
/// this application threw on construction and its harness could not start — the same omission
/// <c>SqsTest</c> and <c>DynamoDbStreamApp</c> had fixed on 2026-08-12. It survived here because
/// this was the one integration app with no test project to start it.
/// </para>
/// </summary>
[HardenedModule]
[LambdaWebModule]
public partial class Application {
    private void Configure(IAppConfig config) {
        config.Amend((ResponseHeaderConfiguration response) => response.Add("Access-Control-Allow-Origin", "*"));
    }
}