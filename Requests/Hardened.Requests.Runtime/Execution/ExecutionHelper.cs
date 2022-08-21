using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Requests.Runtime.Execution
{
    public static class ExecutionHelper
    {
        private static readonly Task<IExecutionRequestParameters> _emptyRequestParameters = Task.FromResult(EmptyParameters.Instance);
        private static readonly Func<IExecutionContext, Task<IExecutionRequestParameters>> 
            _emptyDeserializeRequest = _ =>  _emptyRequestParameters;

        public static IEnumerable<IRequestFilterProvider> GetFilterInfo(params object[] attributes)
        {
            foreach (var attribute in attributes)
            {
                if (attribute is IRequestFilterProvider filterProvider)
                {
                    yield return filterProvider;
                }
            }
        }

        #region sync invoke no parameters

        public delegate void InvokeNoParameters<T>(IExecutionContext context, T controller);

        public static Func<IExecutionContext, IExecutionFilter>[] StandardFilterEmptyParameters<TController>(
            IServiceProvider serviceProvider,
            IExecutionRequestHandlerInfo handlerInfo,
            InvokeNoParameters<TController> invokeMethod,
            IEnumerable<IRequestFilterProvider> filterProviders)
        {
            var ioFilterProvider = serviceProvider.GetRequiredService<IIOFilterProvider>();

            var ioFilter = ioFilterProvider.ProvideFilter(
                handlerInfo,
                _emptyDeserializeRequest
            );
            
            var invokeFilter = new InvokeNoParametersFilter<TController>(invokeMethod);

            return CreateFilterArray(serviceProvider, handlerInfo, filterProviders, ioFilter, invokeFilter);
        }
        
        #endregion

        #region sync invoke parameters
        
        public delegate void InvokeWithParameters<TController, TParameter>(IExecutionContext context, TController controller, TParameter parameter);

        public static Func<IExecutionContext, IExecutionFilter>[] StandardFilterWithParameters<TController, TParameter>(
            IServiceProvider serviceProvider,
            IExecutionRequestHandlerInfo handlerInfo,
            Func<IExecutionContext, Task<IExecutionRequestParameters>> deserializeRequestFunc,
            InvokeWithParameters<TController, TParameter> invokeMethod,
            IEnumerable<IRequestFilterProvider> filterProviders) where TController : class
        {
            var contextSerializationService = serviceProvider.GetRequiredService<IContextSerializationService>();

            var ioFilter = new IOFilter(
                deserializeRequestFunc,
                contextSerializationService.SerializeResponse
            );
            
            var invokeFilter = new InvokeWithParametersFilter<TController, TParameter>(invokeMethod);

            return CreateFilterArray(serviceProvider, handlerInfo, filterProviders, ioFilter, invokeFilter);
        }

        #endregion

        #region async invoke no parameters

        public delegate Task AsyncInvokeNoParameters<TController>(IExecutionContext context, TController controller) where TController : class;

        public static Func<IExecutionContext, IExecutionFilter>[] AsyncStandardFilterEmptyParameters<TController>(
            IServiceProvider serviceProvider,
            IExecutionRequestHandlerInfo handlerInfo,
            AsyncInvokeNoParameters<TController> invokeMethod,
            IEnumerable<IRequestFilterProvider> filterProviders) where TController : class
        {
            var ioFilterProvider = serviceProvider.GetRequiredService<IIOFilterProvider>();

            var ioFilter = ioFilterProvider.ProvideFilter(
                handlerInfo,
                _emptyDeserializeRequest
            );

            var invokeFilter = new AsyncInvokeNoParametersFilter<TController>(invokeMethod);

            return CreateFilterArray(serviceProvider, handlerInfo, filterProviders, ioFilter, invokeFilter);
        }

        #endregion

        #region invoke async parameters

        public delegate Task AsyncInvokeWithParameters<TController, TParameter>(
            IExecutionContext context, TController controller, TParameter parameter) where TController : class where TParameter : class;

        public static Func<IExecutionContext, IExecutionFilter>[] AsyncStandardFilterWithParameters<TController, TParameter>(
            IServiceProvider serviceProvider,
            IExecutionRequestHandlerInfo handlerInfo,
            Func<IExecutionContext, Task<IExecutionRequestParameters>> deserializeRequestFunc,
            AsyncInvokeWithParameters<TController, TParameter> invokeMethod,
            IEnumerable<IRequestFilterProvider> filterProviders) where TController : class where TParameter : class
        {
            var contextSerializationService = serviceProvider.GetRequiredService<IContextSerializationService>();

            var ioFilter = new IOFilter(
                deserializeRequestFunc,
                contextSerializationService.SerializeResponse
            );

            var invokeFilter = new AsyncInvokeWithParametersFilter<TController, TParameter>(invokeMethod);

            return CreateFilterArray(serviceProvider, handlerInfo, filterProviders, ioFilter, invokeFilter);
        }

        #endregion

        #region create filter array

        private static Func<IExecutionContext, IExecutionFilter>[] CreateFilterArray(
            IServiceProvider serviceProvider,
            IExecutionRequestHandlerInfo handlerInfo, 
            IEnumerable<IRequestFilterProvider> filterProviders, 
            IExecutionFilter ioFilter,
            IExecutionFilter invokeFilter)
        {
            var filterList =
                serviceProvider.GetRequiredService<IGlobalFilterRegistry>().GetFilters(handlerInfo);

            filterList.Add(new RequestFilterInfo(_ => ioFilter, FilterOrder.Serialization));

            filterList.Add(new RequestFilterInfo(_ => invokeFilter, FilterOrder.EndPointInvoke));

            foreach (var requestFilterProvider in filterProviders)
            {
                filterList.AddRange(requestFilterProvider.GetFilters(handlerInfo));
            }

            filterList.Sort((x, y) =>
                Comparer<int>.Default.Compare(x.Order ?? FilterOrder.DefaultValue, y.Order ?? FilterOrder.DefaultValue));

            return filterList.Select(f => f.FilterFunc).ToArray();
        }
        #endregion
    }
}
