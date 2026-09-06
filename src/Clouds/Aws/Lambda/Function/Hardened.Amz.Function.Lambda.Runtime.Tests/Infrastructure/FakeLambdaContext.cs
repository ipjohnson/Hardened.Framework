using Amazon.Lambda.Core;

namespace Hardened.Amz.Function.Lambda.Runtime.Tests.Infrastructure;

/// <summary>
/// A minimal <see cref="ILambdaContext"/>. Deliberately not
/// <c>Hardened.Amz.Shared.Lambda.Testing.TestLambdaContext</c>: that is a shipped package with its
/// own test project in another workstream, and this project should measure the runtime rather than
/// the harness.
/// </summary>
public sealed class FakeLambdaContext : ILambdaContext {
    public FakeLambdaContext(string functionName, IDictionary<string, string>? custom = null) {
        FunctionName = functionName;
        ClientContext = custom == null ? null! : new FakeClientContext(custom);
    }

    public string AwsRequestId { get; set; } = "request-id";

    public IClientContext ClientContext { get; set; }

    public string FunctionName { get; set; }

    public string FunctionVersion { get; set; } = "1";

    public ICognitoIdentity Identity { get; set; } = null!;

    public string InvokedFunctionArn { get; set; } = "arn";

    public ILambdaLogger Logger { get; set; } = null!;

    public string LogGroupName { get; set; } = "log-group";

    public string LogStreamName { get; set; } = "log-stream";

    public int MemoryLimitInMB { get; set; } = 1024;

    public TimeSpan RemainingTime { get; set; } = TimeSpan.FromSeconds(60);

    private sealed class FakeClientContext : IClientContext {
        public FakeClientContext(IDictionary<string, string> custom) {
            Custom = custom;
        }

        public IDictionary<string, string> Environment { get; } = new Dictionary<string, string>();

        public IClientApplication Client { get; } = null!;

        public IDictionary<string, string> Custom { get; }
    }
}
