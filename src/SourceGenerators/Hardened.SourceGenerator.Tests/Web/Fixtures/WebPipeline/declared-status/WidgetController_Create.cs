using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Execution;
using System;
using System.Threading.Tasks;
using TestApp;

namespace TestApp.Generated
{
    public partial class WidgetController_Create : global::Hardened.Requests.Runtime.Execution.BaseExecutionHandler<global::TestApp.WidgetController>
    {
        private readonly static global::Hardened.Requests.Runtime.Execution.ExecutionRequestHandlerInfo _handlerInfo =         new ExecutionRequestHandlerInfo("/widgets", "POST", typeof(WidgetController), "Create", successStatus: 201)
;

        public WidgetController_Create(global::System.IServiceProvider serviceProvider, string? routePath = null)
             : base(global::Hardened.Requests.Runtime.Execution.ExecutionHelper.AsyncStandardFilterEmptyParameters<global::TestApp.WidgetController>(
            serviceProvider,
            _handlerInfo.WithPath(routePath),
            InvokeMethod,
            global::Hardened.Requests.Runtime.Execution.ExecutionHelper.GetFilterInfo()
        ), null)
        {
        }

        private static async global::System.Threading.Tasks.Task InvokeMethod(global::Hardened.Requests.Abstract.Execution.IExecutionContext context, global::TestApp.WidgetController controller)
        {
            context.Response.ResponseValue = await controller.Create();
        }
    }
}
