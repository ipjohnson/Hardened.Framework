using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Runtime.Errors;

/// <summary>
/// Where a generated handler puts an exception it caught.
/// </summary>
/// <remarks>
/// <para>
/// Recorded rather than rethrown: the response has to be produced by the same pipeline that
/// produced the request, and the filter at <c>FilterOrder.Serialization</c> is what turns what is
/// on the response into bytes.
/// </para>
/// <para>
/// <b>It no longer logs.</b> It used to, and that made a handler fault the only kind of failure
/// with a log line - an exception thrown by a filter, an authorization refusal and a rate-limit
/// refusal all set the same field and reported nothing. <c>ExceptionResponseSerializer</c> logs
/// instead, because that is the one place all four arrive at. Logging here as well would report a
/// handler fault twice.
/// </para>
/// </remarks>
public static class ControllerErrorHelper {
    public static Task HandleException(IExecutionContext context, Exception exception) {
        context.Response.ExceptionValue = exception;

        return Task.CompletedTask;
    }
}
