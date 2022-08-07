using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Hardened.Function.Lambda.Runtime.Impl;
using Hardened.IntegrationTests.Function.Lambda.SUT.Models;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Requests.Runtime.Execution;
using Hardened.Shared.Lambda.Runtime.Logging;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hardened.IntegrationTests.Function.Lambda.SUT.Functions
{
    public partial class PersonFunctions_GetAllPeople : ILambdaHandler<PersonListModel>
    {
        public class InvokeFilter : BaseExecutionHandler<PersonFunctions>
        {
            private static readonly ExecutionRequestHandlerInfo _handlerInfo = 
                new("GetAllPeople", "Invoke", typeof(PersonFunctions), "GetAllPeople");

            public InvokeFilter(IServiceProvider serviceProvider)
                : base(ExecutionHelper.StandardFilterEmptyParameters<PersonFunctions>(
                    serviceProvider, _handlerInfo, InvokeMethod, ExecutionHelper.GetFilterInfo()))
            {

            }

            private static void InvokeMethod(IExecutionContext context, PersonFunctions controller)
            {
                context.Response.ResponseValue = controller.GetAllPeople();
            }

            public override IExecutionRequestHandlerInfo HandlerInfo => _handlerInfo;
        }

    }
}
