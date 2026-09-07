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

    public TestLambdaContext(
        IDictionary<string, string>? custom = null,
        string functionName = "conformance",
        TimeSpan? remainingTime = null) {
        ClientContext = new TestClientContext(custom ?? new Dictionary<string, string>());
        FunctionName = functionName;
        _remainingTime = remainingTime;
    }

    private readonly TimeSpan? _remainingTime;

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
    /// <summary>
    /// What Lambda says is left of this invocation, when a test set it.
    /// </summary>
    /// <remarks>
    /// Still throws by default. The host turns this into a cancellation deadline, so a test that
    /// does not set it deliberately should not silently get a fabricated one.
    /// </remarks>
    public TimeSpan RemainingTime =>
        _remainingTime ?? throw new NotSupportedException();

    private sealed class TestClientContext : IClientContext {
        public TestClientContext(IDictionary<string, string> custom) {
            Custom = custom;
        }

        public IDictionary<string, string> Custom { get; }

        public IClientApplication Client => throw new NotSupportedException();
        public IDictionary<string, string> Environment => throw new NotSupportedException();
    }
}
