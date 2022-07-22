using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.RequestFilter
{
    public interface IIOFilterProvider
    {
        IExecutionFilter ProvideFilter(
            IExecutionRequestHandlerInfo handlerInfo,
            Func<IExecutionContext, Task<IExecutionRequestParameters>> deserializeRequest);
    }
}
