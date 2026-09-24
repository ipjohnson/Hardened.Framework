using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Runtime.Filters;

/// <summary>
/// What a stream framing can learn about the body it has been writing to.
/// </summary>
internal static class StreamBody
{
    /// <summary>
    /// Whether the stream has put nothing on the wire yet.
    /// </summary>
    /// <remarks>
    /// Asked of the body's position where the body can answer - a buffer the cache or a test
    /// holds - and of the response otherwise. Kestrel's response body throws
    /// <c>NotSupportedException</c> from <c>Position</c>, so reading it unconditionally ended
    /// every event stream on a real socket with a logged fault after the last event had already
    /// gone out. On a transport the filter flushes after every item and every heartbeat, so a
    /// response that has started is one that carried something.
    /// </remarks>
    public static bool NothingWritten(IExecutionContext context)
    {
        var body = context.Response.Body;

        return body.CanSeek ? body.Position == 0 : !context.Response.ResponseStarted;
    }
}
