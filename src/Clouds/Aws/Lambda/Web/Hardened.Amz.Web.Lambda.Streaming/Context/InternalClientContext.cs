using Amazon.Lambda.Core;

namespace Hardened.Amz.Web.Lambda.Streaming.Context;

internal class InternalClientContext : IClientContext {
    public IDictionary<string, string> Environment { get; set; } = new Dictionary<string, string>();
    public IClientApplication Client { get; set; } = new InternalClientApplication();
    public IDictionary<string, string> Custom { get; set; } = new Dictionary<string, string>();
}

internal class InternalClientApplication : IClientApplication {
    public string AppPackageName { get; set; } = string.Empty;
    public string AppTitle { get; set; } = string.Empty;
    public string AppVersionCode { get; set; } = string.Empty;
    public string AppVersionName { get; set; } = string.Empty;
    public string InstallationId { get; set; } = string.Empty;
}

internal class InternalCognitoIdentity : ICognitoIdentity {
    public string IdentityId { get; set; } = string.Empty;
    public string IdentityPoolId { get; set; } = string.Empty;
}
