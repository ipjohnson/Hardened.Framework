using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Execution;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using TestApp;

namespace TestApp.Generated
{
    public partial class ClockController_Now_648 : global::Hardened.Requests.Runtime.Execution.BaseExecutionHandler<global::TestApp.ClockController>
    {
        private readonly static global::Hardened.Requests.Abstract.Execution.IExecutionRequestParameter[] _parameterInfo =         CreateParameterInfo()
;
        private readonly static global::Hardened.Requests.Runtime.Execution.ExecutionRequestHandlerInfo _handlerInfo =         new ExecutionRequestHandlerInfo("/now", "GET", typeof(ClockController), "Now", _parameterInfo)
;

        public ClockController_Now_648(global::System.IServiceProvider serviceProvider, string? routePath = null)
             : base(global::Hardened.Requests.Runtime.Execution.ExecutionHelper.AsyncStandardFilterWithParameters<
            global::TestApp.ClockController,
            Parameters
        >(
            serviceProvider,
            _handlerInfo.WithPath(routePath),
            BindRequestParameters,
            InvokeMethod,
            global::Hardened.Requests.Runtime.Execution.ExecutionHelper.GetFilterInfo()
        ), null)
        {
        }

        private static global::Hardened.Requests.Abstract.Execution.IExecutionRequestParameter[] CreateParameterInfo()
        {
            var returnArray = new global::Hardened.Requests.Abstract.Execution.IExecutionRequestParameter[1];
            returnArray[0] = new global::Hardened.Requests.Runtime.Execution.ExecutionRequestParameter(
                "clock",
                0,
                typeof(global::TestApp.IClock)
            );
            return returnArray;
        }

        private static async global::System.Threading.Tasks.Task InvokeMethod(global::Hardened.Requests.Abstract.Execution.IExecutionContext context, global::TestApp.ClockController controller, Parameters parameters)
        {
            context.Response.ResponseValue = await controller.Now(parameters.clock);
        }

        private static global::System.Threading.Tasks.Task<global::Hardened.Requests.Abstract.Execution.IExecutionRequestParameters> BindRequestParameters(global::Hardened.Requests.Abstract.Execution.IExecutionContext context)
        {
            var parameters = new Parameters();
            parameters.clock = context.RequestServices.GetRequiredService<global::TestApp.IClock>();
            return global::System.Threading.Tasks.Task.FromResult<global::Hardened.Requests.Abstract.Execution.IExecutionRequestParameters>(parameters);
        }

        public partial class Parameters : global::Hardened.Requests.Runtime.Execution.ExecutionRequestParameters
        {

            public global::TestApp.IClock clock { get; set; } = default!;

            public override object this[int index]
            {
                get
                {
                    switch (index)
                    {
                        case 0:
                            return this.clock!;
                    }
                    throw new global::System.IndexOutOfRangeException("Index out of range, parameters count 1, index was " + index);
                }
                set
                {
                    switch (index)
                    {
                        case 0:
                            this.clock = (global::TestApp.IClock)value;
                            return;
                    }
                    throw new global::System.IndexOutOfRangeException("Index out of range, parameters count 1, index was " + index);
                }
            }

            public override global::System.Collections.Generic.IReadOnlyList<global::Hardened.Requests.Abstract.Execution.IExecutionRequestParameter> Info => _parameterInfo;
        }
    }
}
