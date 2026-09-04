using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hardened.Requests.Runtime.Execution;

public static partial class ExecutionHelper {

    /// <summary>
    /// The log category that writes each handler's composed filter chain, at Debug.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One line per handler, as its chain is built: each filter and its order, in the order they
    /// run. What it answers is "did my filter land where I meant?" - a position that reads right
    /// and lands wrong, an attribute that was never carried into the metadata, a global filter
    /// that stood down - which until this existed could only be reconstructed from a stack trace.
    /// </para>
    /// <para>
    /// Its own category so it can be turned on alone, and off by default: a chain is composed
    /// once per handler, so an application that has not enabled it pays one <c>IsEnabled</c>
    /// check per handler and nothing per request.
    /// </para>
    /// </remarks>
    public const string FilterChainLogCategory = "Hardened.Requests.Pipeline";

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
        handlerInfo = handlerInfo.WithTimeout(TimeoutResolver.Resolve(serviceProvider, handlerInfo));

        var filterList =
            serviceProvider.GetRequiredService<IGlobalFilterRegistry>().GetFilters(handlerInfo);

        AddTimeoutFilter(filterList, handlerInfo);

        filterList.Add(new RequestFilterInfo(
            _ => ioFilter, FilterOrder.Serialization, FilterNames.Of(ioFilter)));

        filterList.Add(new RequestFilterInfo(
            _ => invokeFilter, FilterOrder.EndPointInvoke, FilterNames.Of(invokeFilter)));

        filterList.Add(new RequestFilterInfo(
            _ => instanceFilter, FilterOrder.HandlerCreation, FilterNames.Of(instanceFilter)));

        foreach (var requestFilterProvider in filterProviders) {
            filterList.AddRange(requestFilterProvider.GetFilters(handlerInfo));
        }

        // OrderBy rather than List.Sort, because it is stable and List.Sort is not.
        //
        // Two filters at one order is not a mistake to be designed out: FilterOrder.Before and
        // FilterOrder.After name a position rather than a stage, so two registrations asking for
        // the same place get the same integer, which is correct - they asked for the same place.
        // What was wrong was the tie breaking differently between runs, which for anything
        // straddling serialization decides whether a body is written. Registration order is now
        // what settles it, and registration order is deterministic.
        var chain = filterList
            .OrderBy(filter => filter.Order ?? FilterOrder.DefaultValue)
            .ToArray();

        LogFilterChain(serviceProvider, handlerInfo, chain);

        return new ExecutionHandlerSetup(
            handlerInfo,
            Array.ConvertAll(chain, filter => filter.FilterFunc));
    }

    /// <summary>
    /// Writes what <paramref name="chain"/> was composed into, when anything is listening.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The factory is optional and the level is checked before anything is described, so an
    /// application that has not enabled <see cref="FilterChainLogCategory"/> at Debug pays a
    /// service lookup and one boolean per handler, once, and a pipeline assembled without logging
    /// at all - the bare chains some tests build - pays the lookup alone.
    /// </para>
    /// <para>
    /// Written here, where the chain is composed, rather than on the first request that runs it:
    /// the orders are still to hand, and the line arrives whether or not a request ever does.
    /// </para>
    /// </remarks>
    private static void LogFilterChain(
        IServiceProvider serviceProvider,
        IExecutionRequestHandlerInfo handlerInfo,
        RequestFilterInfo[] chain) {
        var logger = serviceProvider.GetService<ILoggerFactory>()?.CreateLogger(FilterChainLogCategory);

        if (logger == null || !logger.IsEnabled(LogLevel.Debug)) {
            return;
        }

        var described = new string[chain.Length];

        for (var i = 0; i < chain.Length; i++) {
            described[i] = FilterNames.Of(chain[i]) + "@" + (chain[i].Order ?? FilterOrder.DefaultValue);
        }

        LogFilterChain(logger, handlerInfo.Method, handlerInfo.Path, string.Join(", ", described));
    }

    [LoggerMessage(
        EventId = 78010,
        Level = LogLevel.Debug,
        Message = "{httpMethod} {path} filter chain: {chain}")]
    private static partial void LogFilterChain(ILogger logger, string httpMethod, string path, string chain);

    /// <summary>
    /// Installs the one filter that enforces whatever the cascade resolved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One place, rather than the attribute providing a filter and the application-wide default
    /// providing another. That is what makes the cascade expressible at all: the assembly and
    /// entry-point rungs have no attribute on the handler to provide anything, and a handler
    /// bounded by two of them would otherwise carry two filters and two timers for one answer.
    /// </para>
    /// <para>
    /// <c>FilterOrder.Before + FilterOrder.Serialization</c>, one half-gap ahead of the bind.
    /// Anything later hands the handler the transport's token, because a declared
    /// <c>CancellationToken</c> parameter is copied out of the context at
    /// <see cref="FilterOrder.Serialization"/> - so the budget would reach nothing. Anything
    /// earlier drags the conditional flush and the response cache's store inside the deadline,
    /// which is what <c>CancellationScope</c>'s restore exists to prevent.
    /// </para>
    /// <para>
    /// One filter instance per handler, shared by every request. A handler nothing bounds gets no
    /// filter and no timer.
    /// </para>
    /// </remarks>
    private static void AddTimeoutFilter(
        List<RequestFilterInfo> filterList, IExecutionRequestHandlerInfo handlerInfo) {
        if (handlerInfo.Timeout is not { } timeout) {
            return;
        }

        var filter = new TimeoutFilter(timeout.Milliseconds);

        filterList.Add(new RequestFilterInfo(
            _ => filter,
            FilterOrder.Before + FilterOrder.Serialization,
            nameof(TimeoutFilter)));
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
    /// The amendment goes through <see cref="ExecutionRequestHandlerInfoExtensions.WithRequirement"/>
    /// rather than reconstructing the handler here. Reconstructing here is what dropped
    /// <c>SuccessStatus</c>, <c>NullResponseBody</c> and <c>ProducedContentTypes</c> for every
    /// application that registered a convention: the call listed seven of the ten arguments, and
    /// nothing about it looked incomplete. One copy path, in the type that owns the members, is
    /// what keeps the next added member from going the same way.
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

        return handlerInfo.WithRequirement(Requirement.AllOf([..requirements]));
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