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
    public class PersonFunctions_GetAllPeople : ILambdaHandler<PersonListModel>
    {
        private readonly ILambdaFunctionImplService _lambdaFunctionImplService;
        private readonly Application _application;

        public PersonFunctions_GetAllPeople()
        : this(new EnvironmentImpl(), null)
        {

        }

        public PersonFunctions_GetAllPeople(IEnvironment environment, Action<IServiceCollection>? overrideDependencies)
        {
            _application = new Application(environment, overrideDependencies);
            var filter = new LambdaInvokeFilter(new InvokeFilter(_application.Provider));

            _application.Provider.GetService<IMiddlewareService>()!.Use(_ => filter);
            _lambdaFunctionImplService = _application.Provider.GetRequiredService<ILambdaFunctionImplService>();
        }
        

        public Task<Stream> Invoke(Stream inputStream, ILambdaContext context)
        {
            return _lambdaFunctionImplService.InvokeFunction(inputStream, context);
        }

        public IServiceProvider Provider => _application.Provider;

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
