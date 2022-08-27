using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Configuration;
using Hardened.Requests.Runtime.Filters;
using Microsoft.Extensions.Options;

namespace Hardened.Requests.Runtime.Execution
{
    public class IOFilterProvider : IIOFilterProvider
    {
        private readonly IContextSerializationService _contextSerializationService;
        private Action<IExecutionContext>? _headerActions;

        public IOFilterProvider(
            IContextSerializationService contextSerializationService,
            IOptions<IResponseHeaderConfiguration> responseHeaderConfiguration)
        {
            _contextSerializationService = contextSerializationService;
            _headerActions = SetupHeaderActions(responseHeaderConfiguration.Value);
        }

        private Action<IExecutionContext>? SetupHeaderActions(IResponseHeaderConfiguration responseHeaderConfiguration)
        {
            if (responseHeaderConfiguration.HeaderActions.Count == 0 &&
                responseHeaderConfiguration.CommonHeaders.Count == 0)
            {
                return null;
            }
            var headerAction = new List<Action<IExecutionContext>>(responseHeaderConfiguration.HeaderActions);

            if (responseHeaderConfiguration.CommonHeaders.Count > 0)
            {
                var commonList = responseHeaderConfiguration.CommonHeaders;

                headerAction.Add(context =>
                {
                    var responseHeaders = context.Response.Headers;

                    for (var i = 0; i < commonList.Count; i++)
                    {
                        var kvp = commonList[i];

                        responseHeaders.Set(kvp.Key, kvp.Value);
                    }
                });
            }

            if (headerAction.Count == 1)
            {
                return headerAction[0];
            }

            return context =>
            {
                for (var i = 0; i < headerAction.Count; i++)
                {
                    headerAction[i].Invoke(context);
                }
            };
        }

        public IExecutionFilter ProvideFilter(
            IExecutionRequestHandlerInfo handlerInfo, 
            Func<IExecutionContext, Task<IExecutionRequestParameters>> deserializeRequest)
        {
            return new IOFilter(
                deserializeRequest,
                _contextSerializationService.SerializeResponse,
                _headerActions
            );
        }
    }
}
