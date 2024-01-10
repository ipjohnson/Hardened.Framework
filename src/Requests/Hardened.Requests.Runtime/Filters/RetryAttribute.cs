using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Shared.Runtime.Collections;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Requests.Runtime.Filters;

public class RetryAttribute : Attribute, IRequestFilterProvider {
    public int Retries { get; set; } = 3;

    public int SleepTime { get; set; } = 500;

    public IEnumerable<RequestFilterInfo> GetFilters(IExecutionRequestHandlerInfo handlerInfo) {
        yield return new RequestFilterInfo(context =>
                new RetryFilter(
                    context.RequestServices.GetRequiredService<IMemoryStreamPool>(),
                    Retries,
                    SleepTime
                ),
            FilterOrder.HandlerCreation - 10);
    }
}