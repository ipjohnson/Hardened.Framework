using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Requests.Runtime.Execution
{

    public static class ExecutionHelper
    {
        #region sync invoke no parameters

        public delegate void InvokeNoParameters<T>(IExecutionContext context, T controller);

        public static Func<IExecutionContext, IExecutionFilter>[] StandardFilterEmptyParameters<TController>(
            IServiceProvider serviceProvider,
            InvokeNoParameters<TController> invokeMethod)
        {
            var contextSerializationService = serviceProvider.GetRequiredService<IContextSerializationService>();

            var filterArray = new Func<IExecutionContext, IExecutionFilter>[2];

            var ioFilter = new IOFilter<object>(
                _ => Task.FromResult(EmptyParameters.Instance),
                contextSerializationService.SerializeResponse
            );

            filterArray[0] = _ => ioFilter;

            var invokeFilter = new InvokeNoParametersFilter<TController>(invokeMethod);

            filterArray[1] = _ => invokeFilter;

            return filterArray;
        }

        #endregion

        #region invoke parameters
        
        public delegate void InvokeWithParameters<TController, TParameter>(IExecutionContext context, TController controller, TParameter parameter);

        public static Func<IExecutionContext, IExecutionFilter>[] StandardFilterWithParameters<TController, TParameter>(
            IServiceProvider serviceProvider,
            InvokeWithParameters<TController, TParameter> invokeMethod) where TController : class
        {
            var contextSerializationService = serviceProvider.GetRequiredService<IContextSerializationService>();

            var filterArray = new Func<IExecutionContext, IExecutionFilter>[2];

            var ioFilter = new IOFilter<object>(
                _ => Task.FromResult(EmptyParameters.Instance),
                contextSerializationService.SerializeResponse
            );

            filterArray[0] = _ => ioFilter;

            var invokeFilter = new InvokeWithParametersFilter<TController, TParameter>(invokeMethod);

            filterArray[1] = _ => invokeFilter;

            return filterArray;
        }

        #endregion

        #region async invoke no parameters

        public delegate Task AsyncInvokeNoParameters<TController>(IExecutionContext context, TController controller) where TController : class;

        public static Func<IExecutionContext, IExecutionFilter>[] AsyncStandardFilterEmptyParameters<TController>(
            IServiceProvider serviceProvider,
            AsyncInvokeNoParameters<TController> invokeMethod) where TController : class
        {
            var contextSerializationService = serviceProvider.GetRequiredService<IContextSerializationService>();

            var filterArray = new Func<IExecutionContext, IExecutionFilter>[2];

            var ioFilter = new IOFilter<object>(
                _ => Task.FromResult(EmptyParameters.Instance),
                contextSerializationService.SerializeResponse
            );

            filterArray[0] = _ => ioFilter;

            var invokeFilter = new AsyncInvokeNoParametersFilter<TController>(invokeMethod);

            filterArray[1] = _ => invokeFilter;

            return filterArray;
        }

        #endregion

        #region invoke async parameters

        public delegate Task AsyncInvokeWithParameters<TController, TParameter>(
            IExecutionContext context, TController controller, TParameter parameter) where TController : class where TParameter : class;

        public static Func<IExecutionContext, IExecutionFilter>[] AsyncStandardFilterWithParameters<TController, TParameter>(
            IServiceProvider serviceProvider,
            AsyncInvokeWithParameters<TController, TParameter> invokeMethod) where TController : class where TParameter : class
        {
            var contextSerializationService = serviceProvider.GetRequiredService<IContextSerializationService>();

            var filterArray = new Func<IExecutionContext, IExecutionFilter>[2];

            var ioFilter = new IOFilter<object>(
                _ => Task.FromResult(EmptyParameters.Instance),
                contextSerializationService.SerializeResponse
            );

            filterArray[0] = _ => ioFilter;

            var invokeFilter = new AsyncInvokeWithParametersFilter<TController, TParameter>(invokeMethod);

            filterArray[1] = _ => invokeFilter;

            return filterArray;
        }

        #endregion
    }
}
