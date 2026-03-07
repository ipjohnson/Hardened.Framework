using Amazon.Lambda.Core;

namespace Hardened.Amz.Function.Lambda.Streaming.Context;

internal class InternalLambdaContext : ILambdaContext {
    private readonly long _deadlineMs;

    public InternalLambdaContext(
        string awsRequestId,
        string invokedFunctionArn,
        long deadlineMs,
        string functionName,
        string functionVersion,
        string logGroupName,
        string logStreamName,
        int memoryLimitInMb) {
        _deadlineMs = deadlineMs;
        AwsRequestId = awsRequestId;
        InvokedFunctionArn = invokedFunctionArn;
        FunctionName = functionName;
        FunctionVersion = functionVersion;
        LogGroupName = logGroupName;
        LogStreamName = logStreamName;
        MemoryLimitInMB = memoryLimitInMb;
        Logger = new InternalLambdaLogger();
        ClientContext = new InternalClientContext();
        Identity = new InternalCognitoIdentity();
    }

    public string AwsRequestId { get; }
    public IClientContext ClientContext { get; }
    public string FunctionName { get; }
    public string FunctionVersion { get; }
    public ICognitoIdentity Identity { get; }
    public string InvokedFunctionArn { get; }
    public ILambdaLogger Logger { get; }
    public string LogGroupName { get; }
    public string LogStreamName { get; }
    public int MemoryLimitInMB { get; }

    public TimeSpan RemainingTime {
        get {
            if (_deadlineMs <= 0) return TimeSpan.MaxValue;

            var epochNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var remaining = _deadlineMs - epochNow;
            return remaining > 0
                ? TimeSpan.FromMilliseconds(remaining)
                : TimeSpan.Zero;
        }
    }
}
