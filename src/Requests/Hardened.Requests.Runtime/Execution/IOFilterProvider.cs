using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Configuration;
using Hardened.Requests.Runtime.Filters;
using Hardened.Requests.Runtime.Streaming;
using Microsoft.Extensions.Options;

namespace Hardened.Requests.Runtime.Execution;

[SingletonService(Using = RegistrationType.Try)]
public class IOFilterProvider : IIOFilterProvider {
    private readonly IContextSerializationService _contextSerializationService;
    private readonly Action<IExecutionContext>? _headerActions;
    private readonly TimeSpan _heartbeatInterval;

    public IOFilterProvider(
        IContextSerializationService contextSerializationService,
        IOptions<IResponseHeaderConfiguration> responseHeaderConfiguration,
        IOptions<IStreamingConfiguration> streamingConfiguration) {
        _contextSerializationService = contextSerializationService;
        _headerActions = SetupHeaderActions(responseHeaderConfiguration.Value);
        _heartbeatInterval = streamingConfiguration.Value.HeartbeatInterval;
    }

    private Action<IExecutionContext>? SetupHeaderActions(IResponseHeaderConfiguration responseHeaderConfiguration) {
        if (responseHeaderConfiguration.HeaderActions.Count == 0 &&
            responseHeaderConfiguration.CommonHeaders.Count == 0) {
            return null;
        }

        var headerAction = new List<Action<IExecutionContext>>(responseHeaderConfiguration.HeaderActions);

        if (responseHeaderConfiguration.CommonHeaders.Count > 0) {
            var commonList = responseHeaderConfiguration.CommonHeaders;

            headerAction.Add(context => {
                var responseHeaders = context.Response.Headers;

                for (var i = 0; i < commonList.Count; i++) {
                    var kvp = commonList[i];

                    responseHeaders[kvp.Key] = kvp.Value;
                }
            });
        }

        if (headerAction.Count == 1) {
            return headerAction[0];
        }

        return context => {
            for (var i = 0; i < headerAction.Count; i++) {
                headerAction[i].Invoke(context);
            }
        };
    }

    public IExecutionFilter ProvideFilter(
        IExecutionRequestHandlerInfo handlerInfo,
        Func<IExecutionContext, Task<IExecutionRequestParameters>> deserializeRequest) {
        return new IoFilter(
            deserializeRequest,
            _contextSerializationService.SerializeResponse,
            _headerActions
        );
    }

    public IExecutionFilter ProvideAsyncEnumerableFilter<TItem>(
        IExecutionRequestHandlerInfo handlerInfo,
        Func<IExecutionContext, Task<IExecutionRequestParameters>> deserializeRequest) {
        return ProvideAsyncEnumerableFilter<TItem>(handlerInfo, deserializeRequest, null);
    }

    /// <summary>
    /// The streamed filter, framed the way the handler asked for, with the configured heartbeat.
    /// </summary>
    /// <remarks>
    /// An overload rather than a changed signature: <c>IIOFilterProvider</c> is public, and a
    /// generator emitting the three-argument call is the shape every already-generated application
    /// carries. The generator emits this one when a handler names a framing.
    /// </remarks>
    public IExecutionFilter ProvideAsyncEnumerableFilter<TItem>(
        IExecutionRequestHandlerInfo handlerInfo,
        Func<IExecutionContext, Task<IExecutionRequestParameters>> deserializeRequest,
        IStreamFraming? framing) {
        return new AsyncEnumerableIoFilter<TItem>(
            deserializeRequest,
            _contextSerializationService.SerializeResponse,
            _headerActions,
            framing,
            _heartbeatInterval
        );
    }
}
