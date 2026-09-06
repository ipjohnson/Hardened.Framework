using Amazon.Lambda.Core;

namespace Hardened.Aws.Lambda.Runtime.Tests.Infrastructure;

/// <summary>
/// Enough <see cref="ILambdaContext"/> to build a request.
/// </summary>
/// <remarks>
/// Every member no adapter reads throws rather than answering a plausible default, so a change that
/// starts reading one says so here instead of silently binding a fabricated value into a request.
/// </remarks>
public sealed class TestLambdaContext : ILambdaContext {
    public static readonly TestLambdaContext Instance = new();

    public TestLambdaContext(IDictionary<string, string>? custom = null, string functionName = "conformance") {
        ClientContext = new TestClientContext(custom ?? new Dictionary<string, string>());
        FunctionName = functionName;
    }

    public string FunctionName { get; }

    public IClientContext ClientContext { get; }

    public string AwsRequestId => throw new NotSupportedException();
    public string FunctionVersion => throw new NotSupportedException();
    public ICognitoIdentity Identity => throw new NotSupportedException();
    public string InvokedFunctionArn => throw new NotSupportedException();
    public ILambdaLogger Logger => throw new NotSupportedException();
    public string LogGroupName => throw new NotSupportedException();
    public string LogStreamName => throw new NotSupportedException();
    public int MemoryLimitInMB => throw new NotSupportedException();
    public TimeSpan RemainingTime => throw new NotSupportedException();

    private sealed class TestClientContext : IClientContext {
        public TestClientContext(IDictionary<string, string> custom) {
            Custom = custom;
        }

        public IDictionary<string, string> Custom { get; }

        public IClientApplication Client => throw new NotSupportedException();
        public IDictionary<string, string> Environment => throw new NotSupportedException();
    }
}
