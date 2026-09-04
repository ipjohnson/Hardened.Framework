namespace Hardened.Requests.Runtime.Filters;

/// <summary>
/// The handler a request matched could not be constructed.
/// </summary>
/// <remarks>
/// <para>
/// A constructor parameter no registration satisfies is the usual cause. It is found here rather
/// than at startup because a handler is constructed on the first request its route matches, and
/// the inner exception is the container's, naming the service.
/// </para>
/// <para>
/// <b>Recorded on the response rather than thrown.</b> <c>FilterOrder.HandlerCreation</c> is the
/// outermost position there is, ahead of the filter that turns a failure into bytes, and a filter
/// on that side of the line refuses by recording and continuing. Thrown, this unwound past every
/// filter, so the message reached the log and the caller got a 500 with nothing in it. Recorded,
/// the caller gets the framework's error envelope and <c>IRequestLogger.RequestFailed</c> still
/// logs it with its stack, as it does for a handler that threw.
/// </para>
/// </remarks>
public class HandlerCreationException : InvalidOperationException {

    public HandlerCreationException(string handler, Type handlerType, Exception inner)
        : base($"{handler} could not construct its handler {handlerType.FullName}: {inner.Message}",
            inner) {
        Handler = handler;
        HandlerType = handlerType;
    }

    /// <summary>The handler that could not be constructed, as "METHOD /path".</summary>
    public string Handler { get; }

    /// <summary>The type the container was asked for.</summary>
    public Type HandlerType { get; }
}
