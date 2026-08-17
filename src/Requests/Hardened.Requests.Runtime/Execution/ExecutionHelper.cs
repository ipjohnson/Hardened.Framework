using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Abstract.Serializer;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Requests.Runtime.Execution;

public static class ExecutionHelper {
    private static readonly Task<IExecutionRequestParameters> _emptyRequestParameters =
        Task.FromResult(EmptyParameters.Instance);

    private static readonly Func<IExecutionContext, Task<IExecutionRequestParameters>>
        _emptyDeserializeRequest = _ => _emptyRequestParameters;

    public static IEnumerable<IRequestFilterProvider> GetFilterInfo(params object[] attributes) {
        foreach (var attribute in attributes) {
            if (attribute is IRequestFilterProvider filterProvider) {
                yield return filterProvider;
            }
        }
    }

    #region sync invoke no parameters

    public delegate void InvokeNoParameters<T>(IExecutionContext context, T controller);

    public static ExecutionHandlerSetup StandardFilterEmptyParameters<TController>(
        IServiceProvider serviceProvider,
        IExecutionRequestHandlerInfo handlerInfo,
        InvokeNoParameters<TController> invokeMethod,
        IEnumerable<IRequestFilterProvider> filterProviders) {
        var ioFilterProvider = serviceProvider.GetRequiredService<IIOFilterProvider>();

        var ioFilter = ioFilterProvider.ProvideFilter(
            handlerInfo,
            _emptyDeserializeRequest
        );

        var invokeFilter = new InvokeNoParametersFilter<TController>(invokeMethod);

        var instanceFilter = serviceProvider.GetRequiredService<IInstanceFilterProvider>()
            .ProvideFilter<TController>(serviceProvider);
        
        return CreateFilterArray(serviceProvider, handlerInfo, filterProviders, ioFilter, invokeFilter, instanceFilter);
    }

    #endregion

    #region sync invoke parameters

    public delegate void InvokeWithParameters<TController, TParameter>(IExecutionContext context,
        TController controller, TParameter parameter);

    public static ExecutionHandlerSetup StandardFilterWithParameters<TController, TParameter>(
        IServiceProvider serviceProvider,
        IExecutionRequestHandlerInfo handlerInfo,
        Func<IExecutionContext, Task<IExecutionRequestParameters>> deserializeRequestFunc,
        InvokeWithParameters<TController, TParameter> invokeMethod,
        IEnumerable<IRequestFilterProvider> filterProviders) where TController : class {
        var ioFilterProvider = serviceProvider.GetRequiredService<IIOFilterProvider>();

        var ioFilter = ioFilterProvider.ProvideFilter(
            handlerInfo,
            deserializeRequestFunc
        );

        var invokeFilter = new InvokeWithParametersFilter<TController, TParameter>(invokeMethod);

        var instanceFilter = serviceProvider.GetRequiredService<IInstanceFilterProvider>()
            .ProvideFilter<TController>(serviceProvider);
        
        return CreateFilterArray(serviceProvider, handlerInfo, filterProviders, ioFilter, invokeFilter, instanceFilter);
    }

    #endregion

    #region async invoke no parameters

    public delegate Task AsyncInvokeNoParameters<TController>(IExecutionContext context, TController controller)
        where TController : class;

    public static ExecutionHandlerSetup AsyncStandardFilterEmptyParameters<TController>(
        IServiceProvider serviceProvider,
        IExecutionRequestHandlerInfo handlerInfo,
        AsyncInvokeNoParameters<TController> invokeMethod,
        IEnumerable<IRequestFilterProvider> filterProviders) where TController : class {
        var ioFilterProvider = serviceProvider.GetRequiredService<IIOFilterProvider>();

        var ioFilter = ioFilterProvider.ProvideFilter(
            handlerInfo,
            _emptyDeserializeRequest
        );

        var invokeFilter = new AsyncInvokeNoParametersFilter<TController>(invokeMethod);
        
        var instanceFilter = serviceProvider.GetRequiredService<IInstanceFilterProvider>()
            .ProvideFilter<TController>(serviceProvider);

        return CreateFilterArray(serviceProvider, handlerInfo, filterProviders, ioFilter, invokeFilter, instanceFilter);
    }

    #endregion

    #region invoke async parameters

    public delegate Task AsyncInvokeWithParameters<TController, TParameter>(
        IExecutionContext context, TController controller, TParameter parameter)
        where TController : class where TParameter : class;

    public static ExecutionHandlerSetup
AsyncStandardFilterWithParameters<TController, TParameter>(
            IServiceProvider serviceProvider,
            IExecutionRequestHandlerInfo handlerInfo,
            Func<IExecutionContext, Task<IExecutionRequestParameters>> deserializeRequestFunc,
            AsyncInvokeWithParameters<TController, TParameter> invokeMethod,
            IEnumerable<IRequestFilterProvider> filterProviders) where TController : class where TParameter : class {
        var ioFilterProvider = serviceProvider.GetRequiredService<IIOFilterProvider>();

        var ioFilter = ioFilterProvider.ProvideFilter(
            handlerInfo,
            deserializeRequestFunc
        );

        var invokeFilter = new AsyncInvokeWithParametersFilter<TController, TParameter>(invokeMethod);
        
        var instanceFilter = serviceProvider.GetRequiredService<IInstanceFilterProvider>()
            .ProvideFilter<TController>(serviceProvider);
        
        return CreateFilterArray(serviceProvider, handlerInfo, filterProviders, ioFilter, invokeFilter, instanceFilter);
    }

    #endregion

    #region async enumerable with parameters

    public static ExecutionHandlerSetup
AsyncEnumerableFilterWithParameters<TController, TParameter, TItem>(
            IServiceProvider serviceProvider,
            IExecutionRequestHandlerInfo handlerInfo,
            Func<IExecutionContext, Task<IExecutionRequestParameters>> deserializeRequestFunc,
            InvokeWithParameters<TController, TParameter> invokeMethod,
            IEnumerable<IRequestFilterProvider> filterProviders,
            IStreamFraming? framing = null) where TController : class {
        var ioFilterProvider = serviceProvider.GetRequiredService<IIOFilterProvider>();

        var ioFilter = ioFilterProvider.ProvideAsyncEnumerableFilter<TItem>(
            handlerInfo,
            deserializeRequestFunc,
            framing
        );

        var invokeFilter = new InvokeWithParametersFilter<TController, TParameter>(invokeMethod);

        var instanceFilter = serviceProvider.GetRequiredService<IInstanceFilterProvider>()
            .ProvideFilter<TController>(serviceProvider);

        return CreateFilterArray(serviceProvider, handlerInfo, filterProviders, ioFilter, invokeFilter, instanceFilter);
    }

    #endregion

    #region async enumerable no parameters

    public static ExecutionHandlerSetup
AsyncEnumerableFilterEmptyParameters<TController, TItem>(
            IServiceProvider serviceProvider,
            IExecutionRequestHandlerInfo handlerInfo,
            InvokeNoParameters<TController> invokeMethod,
            IEnumerable<IRequestFilterProvider> filterProviders,
            IStreamFraming? framing = null) {
        var ioFilterProvider = serviceProvider.GetRequiredService<IIOFilterProvider>();

        var ioFilter = ioFilterProvider.ProvideAsyncEnumerableFilter<TItem>(
            handlerInfo,
            _emptyDeserializeRequest,
            framing
        );

        var invokeFilter = new InvokeNoParametersFilter<TController>(invokeMethod);

        var instanceFilter = serviceProvider.GetRequiredService<IInstanceFilterProvider>()
            .ProvideFilter<TController>(serviceProvider);

        return CreateFilterArray(serviceProvider, handlerInfo, filterProviders, ioFilter, invokeFilter, instanceFilter);
    }

    #endregion

    #region create filter array

    /// <remarks>
    /// <para>
    /// Every overload above funnels through here, which is why conventions are applied at the top of
    /// it rather than in each of them. It is also the only point at which both halves are available:
    /// the handler the generator declared, and the service provider the conventions live in.
    /// </para>
    /// <para>
    /// <b>Ahead of the global filter registry, and that ordering is the whole mechanism.</b> The
    /// registry is what asks <c>AuthorizationFilterProvider</c> for this handler's guard, and it is
    /// handed the amended handler - so a requirement a convention added is indistinguishable from
    /// one an attribute declared by the time anything decides what to enforce.
    /// </para>
    /// </remarks>
    private static ExecutionHandlerSetup CreateFilterArray(
        IServiceProvider serviceProvider,
        IExecutionRequestHandlerInfo handlerInfo,
        IEnumerable<IRequestFilterProvider> filterProviders,
        IExecutionFilter ioFilter,
        IExecutionFilter invokeFilter,
        IExecutionFilter instanceFilter) {
        handlerInfo = ApplyConventions(serviceProvider, handlerInfo);

        var filterList =
            serviceProvider.GetRequiredService<IGlobalFilterRegistry>().GetFilters(handlerInfo);

        filterList.Add(new RequestFilterInfo(_ => ioFilter, FilterOrder.Serialization));

        filterList.Add(new RequestFilterInfo(_ => invokeFilter, FilterOrder.EndPointInvoke));

        filterList.Add(new RequestFilterInfo(_ => instanceFilter, FilterOrder.HandlerCreation));
        
        foreach (var requestFilterProvider in filterProviders) {
            filterList.AddRange(requestFilterProvider.GetFilters(handlerInfo));
        }

        filterList.Sort((x, y) =>
            Comparer<int>.Default.Compare(x.Order ?? FilterOrder.DefaultValue, y.Order ?? FilterOrder.DefaultValue));

        return new ExecutionHandlerSetup(handlerInfo, filterList.Select(f => f.FilterFunc).ToArray());
    }

    /// <summary>
    /// Conjoins whatever the conventions require onto what the handler declared.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Conjoined rather than substituted, so a convention can only ever narrow. The declared
    /// requirement goes in first, which costs nothing at evaluation - <c>AllOf</c> flattens, so this
    /// is one node however many contributed - but keeps the rendered description reading in the
    /// order somebody would explain it: what the handler asked for, then what was imposed on it.
    /// </para>
    /// <para>
    /// A handler no convention spoke about is returned untouched, which is the common case and the
    /// case worth not allocating for. Resolving through <c>GetServices</c> rather than requiring a
    /// registration means an application with no conventions pays one empty enumeration per handler
    /// at startup and nothing at all per request.
    /// </para>
    /// <para>
    /// The amended handler is a plain <see cref="ExecutionRequestHandlerInfo"/> rather than a
    /// wrapper delegating to the declared one. Every member of the interface is copied, so the two
    /// carry the same answers - and the status members, the only ones with default implementations
    /// an implementation could have overridden, are answered by that default everywhere in the tree.
    /// A wrapper would be the right shape only once something needs to override one.
    /// </para>
    /// </remarks>
    private static IExecutionRequestHandlerInfo ApplyConventions(
        IServiceProvider serviceProvider, IExecutionRequestHandlerInfo handlerInfo) {
        // GetService rather than GetServices, which resolves IEnumerable<T> as *required* and throws
        // outright on a container that does not synthesise an empty one for an unregistered service.
        // Hardened's does not, so the convenience overload turns "this application registered no
        // conventions" - the overwhelmingly common case - into a failure to construct any handler at
        // all.
        var conventions = serviceProvider.GetService<IEnumerable<IAuthorizationConvention>>();

        if (conventions == null) {
            return handlerInfo;
        }

        List<Requirement>? requirements = null;

        foreach (var convention in conventions) {
            var requirement = convention.Apply(handlerInfo);

            if (requirement != null) {
                (requirements ??= []).Add(requirement);
            }
        }

        if (requirements == null) {
            return handlerInfo;
        }

        if (handlerInfo.Requirement != null) {
            requirements.Insert(0, handlerInfo.Requirement);
        }

        return new ExecutionRequestHandlerInfo(
            handlerInfo.Path,
            handlerInfo.Method,
            handlerInfo.HandlerType,
            handlerInfo.InvokeMethod,
            handlerInfo.Parameters,
            handlerInfo.Metadata,
            Requirement.AllOf([..requirements]));
    }

    #endregion

    public static ValueTask<T> CustomAttributeData<T>(IExecutionContext context, object attribute, IExecutionRequestParameter parameter) {
        if (attribute is ICustomBindingAttribute customBindingAttribute) {
            return customBindingAttribute.BindValue<T>(context, parameter);
        }
        else {
            throw new Exception($"Attribute type {attribute.GetType().FullName} does not implement ICustomBindingAttribute");
        }
    }
}