using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Filters;

namespace Hardened.Requests.Runtime.Execution
{
    public class IOFilterProvider : IIOFilterProvider
    {
        private readonly IContextSerializationService _contextSerializationService;

        public IOFilterProvider(IContextSerializationService contextSerializationService)
        {
            _contextSerializationService = contextSerializationService;
        }

        public IExecutionFilter ProvideFilter(
            IExecutionRequestHandlerInfo handlerInfo, 
            Func<IExecutionContext, Task<IExecutionRequestParameters>> deserializeRequest)
        {
            return new IOFilter(
                deserializeRequest,
                _contextSerializationService.SerializeResponse
            );
        }
    }
}
