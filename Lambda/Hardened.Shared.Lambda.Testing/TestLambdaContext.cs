using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Amazon.Lambda.Core;

namespace Hardened.Shared.Lambda.Testing
{
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

        public static TestLambdaContext FromHandlerType(Type type, IDictionary<string,string>? customData = null)
        {
            var name = type.Name.Split('_').Last();

            return new TestLambdaContext(
                Guid.NewGuid().ToString(),
                new TestClientContext(customData ?? new Dictionary<string, string>()),
                name,
                "1",
                new TestCognitoIdentity(),
                "arn",
                new TestLambdaLogger(),
                name + "Logger",
                name + "LoggerStream",
                1024,
                TimeSpan.FromSeconds(60)
                );
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

        public TimeSpan RemainingTime { get; }
    }
}
