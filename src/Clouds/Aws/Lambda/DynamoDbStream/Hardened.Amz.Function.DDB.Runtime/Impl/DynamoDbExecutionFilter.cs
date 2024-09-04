using Amazon.Lambda.DynamoDBEvents;
using Hardened.Requests.Abstract.Execution;

namespace Hardened.Amz.Function.DDB.Runtime;

public class DynamoDbExecutionFilter : IExecutionFilter {

    public Task Execute(IExecutionChain chain) {
        DynamoDBEvent.DynamodbStreamRecord record;

        return chain.Next();
    }
}