using Amazon.Lambda.Core;

namespace Hardened.Amz.Shared.Lambda.Testing;

public class TestLambdaContext : ILambdaContext
{

    public TestLambdaContext(
        string awsRequestId,
        IClientContext clientContext,
        string functionName,
        string functionVersion,
        ICognitoIdentity identity,
        string invokedFunctionArn,
        ILambdaLogger logger,
        string logGroupName,
        string logStreamName,
        int memoryLimitInMb,
        TimeSpan remainingTime)
    {
        AwsRequestId = awsRequestId;
        ClientContext = clientContext;
        FunctionName = functionName;
        FunctionVersion = functionVersion;
        Identity = identity;
        InvokedFunctionArn = invokedFunctionArn;
        Logger = logger;
        LogGroupName = logGroupName;
        LogStreamName = logStreamName;
        MemoryLimitInMB = memoryLimitInMb;
        RemainingTime = remainingTime;
    }

    public static TestLambdaContext FromName(string functionName, IDictionary<string,string>? customData = null)
    {
        return new TestLambdaContext(
            Guid.NewGuid().ToString(),
            new TestClientContext(customData ?? new Dictionary<string, string>()),
            functionName,
            "1",
            new TestCognitoIdentity(),
            "arn",
            new TestLambdaLogger(),
            functionName + "Logger",
            functionName + "LoggerStream",
            1024,
            TimeSpan.FromSeconds(60)
        );
    }

    public string AwsRequestId { get; set; }

    public IClientContext ClientContext { get; set; }

    public string FunctionName { get; set; }

    public string FunctionVersion { get; set; }

    public ICognitoIdentity Identity { get; set; }

    public string InvokedFunctionArn { get; set; }

    public ILambdaLogger Logger { get; set; }

    public string LogGroupName { get; set; }

    public string LogStreamName { get; set; }

    public int MemoryLimitInMB { get; set; }

    public TimeSpan RemainingTime { get; set; }
}