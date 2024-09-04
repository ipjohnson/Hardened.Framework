using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;

namespace Hardened.Amz.Function.DDB.Runtime.Attributes;

public class DynamoStreamAttribute(string streamName) : Attribute, IRequestFilterProvider {
    public string StreamName { get; } = streamName;

    public IEnumerable<RequestFilterInfo> GetFilters(IExecutionRequestHandlerInfo handlerInfo) {
        yield break;
    }
}