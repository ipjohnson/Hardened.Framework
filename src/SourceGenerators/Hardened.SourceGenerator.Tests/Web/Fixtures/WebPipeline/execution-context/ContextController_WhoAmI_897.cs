using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Execution;
using System;
using System.Threading.Tasks;
using TestApp;

namespace TestApp.Generated
{
    public partial class ContextController_WhoAmI_897 : global::Hardened.Requests.Runtime.Execution.BaseExecutionHandler<global::TestApp.ContextController>
    {
        private readonly static global::Hardened.Requests.Abstract.Execution.IExecutionRequestParameter[] _parameterInfo =         CreateParameterInfo()
;
        private readonly static global::Hardened.Requests.Runtime.Execution.ExecutionRequestHandlerInfo _handlerInfo =         new ExecutionRequestHandlerInfo("/whoami", "GET", typeof(ContextController), "WhoAmI", _parameterInfo)
;

        public ContextController_WhoAmI_897(global::System.IServiceProvider serviceProvider, string? routePath = null)
             : base(global::Hardened.Requests.Runtime.Execution.ExecutionHelper.AsyncStandardFilterWithParameters<
            global::TestApp.ContextController,
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
                "context",
                0,
                typeof(global::Hardened.Requests.Abstract.Execution.IExecutionContext)
            );
            return returnArray;
        }

        private static async global::System.Threading.Tasks.Task InvokeMethod(global::Hardened.Requests.Abstract.Execution.IExecutionContext context, global::TestApp.ContextController controller, Parameters parameters)
        {
            context.Response.ResponseValue = await controller.WhoAmI(parameters.context);
        }

        private static global::System.Threading.Tasks.Task<global::Hardened.Requests.Abstract.Execution.IExecutionRequestParameters> BindRequestParameters(global::Hardened.Requests.Abstract.Execution.IExecutionContext context)
        {
            var parameters = new Parameters();
            parameters.context = context;
            return global::System.Threading.Tasks.Task.FromResult<global::Hardened.Requests.Abstract.Execution.IExecutionRequestParameters>(parameters);
        }

        public partial class Parameters : global::Hardened.Requests.Runtime.Execution.ExecutionRequestParameters
        {

            public global::Hardened.Requests.Abstract.Execution.IExecutionContext context { get; set; } = default!;

            public override object this[int index]
            {
                get
                {
                    switch (index)
                    {
                        case 0:
                            return this.context!;
                    }
                    throw new global::System.IndexOutOfRangeException("Index out of range, parameters count 1, index was " + index);
                }
                set
                {
                    switch (index)
                    {
                        case 0:
                            this.context = (global::Hardened.Requests.Abstract.Execution.IExecutionContext)value;
                            return;
                    }
                    throw new global::System.IndexOutOfRangeException("Index out of range, parameters count 1, index was " + index);
                }
            }

            public override global::System.Collections.Generic.IReadOnlyList<global::Hardened.Requests.Abstract.Execution.IExecutionRequestParameter> Info => _parameterInfo;
        }
    }
}
