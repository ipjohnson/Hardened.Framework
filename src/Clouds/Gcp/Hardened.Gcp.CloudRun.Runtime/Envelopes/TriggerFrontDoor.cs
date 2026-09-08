using System.Globalization;
using Hardened.Gcp.CloudRun.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;

namespace Hardened.Gcp.CloudRun.Runtime.Envelopes;

/// <summary>
/// The filter ahead of routing that recognises an envelope and forks the chain with the trigger
/// request built from it.
/// </summary>
/// <remarks>
/// <para>
/// Every request a Cloud Run service receives passes through here before the routing table sees
/// it. A request no envelope recognises costs one pass over the envelopes' header checks and goes
/// on untouched. A request one recognises has its body buffered and is offered to each of them;
/// the first to unwrap it wins, and the rest of the chain runs again on a fork whose request is
/// the trigger - <c>QUEUE /orders</c> where the wire carried <c>POST /</c> - exactly the way the
/// batch filter runs the rest of the chain once per item. The original chain is not continued:
/// the push was answered by its fork, and routing the raw POST as well would answer it twice.
/// </para>
/// <para>
/// <b>A new request rather than a clone.</b> <c>IExecutionRequest.Clone</c> can rebind the method,
/// the path and the headers of what Kestrel built, but the body it carries is the socket's, and an
/// envelope's whole point is that the handler's body is inside it. <see cref="CloudRunTriggerRequest"/>
/// wraps the delivery and owns its own bytes.
/// </para>
/// <para>
/// A recognised request whose body turns out not to be an envelope gets it back: the bytes are
/// handed on as the body, rewound, so a web route that happens to take a JSON POST is served as if
/// nothing had looked. What it paid is the buffering, which is why an envelope's recognition is
/// asked to be specific.
/// </para>
/// </remarks>
public sealed class TriggerFrontDoor : IExecutionFilter {
    /// <summary>
    /// The most an envelope is allowed to be. A Pub/Sub message is at most ten mebibytes, which
    /// base64 grows by a third; anything past this is not a push and is answered 413 rather than
    /// buffered further.
    /// </summary>
    public const int BufferLimit = 16 * 1024 * 1024;

    private readonly ITriggerEnvelope[] _envelopes;

    public TriggerFrontDoor(IEnumerable<ITriggerEnvelope> envelopes) {
        _envelopes = envelopes.ToArray();
    }

    /// <summary>The envelopes this front door asks, in registration order.</summary>
    public IReadOnlyList<ITriggerEnvelope> Envelopes => _envelopes;

    public async Task Execute(IExecutionChain chain) {
        var context = chain.Context;
        var request = context.Request;

        // A chain composed with this filter twice would otherwise unwrap what it already built.
        if (request is CloudRunTriggerRequest || !Recognised(request, out var candidates)) {
            await chain.Next();

            return;
        }

        if (DeclaredLength(request) > BufferLimit) {
            await chain.Next();

            return;
        }

        var body = await Buffer(request.Body, context.CancellationToken);

        if (body == null) {
            // The body has been read past the limit, so nothing behind this could serve the
            // request faithfully; too large is the honest answer.
            context.Response.Status = 413;
            context.Response.ShouldSerialize = false;

            return;
        }

        CloudRunTriggerRequest? trigger;

        using (var payload = new TriggerPayload(body.Value)) {
            trigger = Unwrap(candidates, request, payload);

            if (trigger == null) {
                request.Body = payload.AsStream();
            }
        }

        if (trigger == null) {
            await chain.Next();

            return;
        }

        await chain.Fork(context.Clone(request: trigger)).Next();
    }

    private bool Recognised(IExecutionRequest request, out List<ITriggerEnvelope> candidates) {
        candidates = null!;

        foreach (var envelope in _envelopes) {
            if (envelope.Recognises(request)) {
                (candidates ??= new List<ITriggerEnvelope>(_envelopes.Length)).Add(envelope);
            }
        }

        return candidates != null;
    }

    private static CloudRunTriggerRequest? Unwrap(
        List<ITriggerEnvelope> candidates, IExecutionRequest request, TriggerPayload payload) {
        foreach (var envelope in candidates) {
            var trigger = envelope.Unwrap(request, payload);

            if (trigger != null) {
                return trigger;
            }
        }

        return null;
    }

    private static long DeclaredLength(IExecutionRequest request) =>
        request.Headers.TryGetValue(KnownHeaders.ContentLength, out var value) &&
        long.TryParse(value.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var length)
            ? length
            : -1;

    /// <summary>
    /// The whole body, or null once it has exceeded <see cref="BufferLimit"/>.
    /// </summary>
    private static async Task<ReadOnlyMemory<byte>?> Buffer(Stream body, CancellationToken cancellationToken) {
        var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];

        while (true) {
            var read = await body.ReadAsync(chunk, cancellationToken);

            if (read == 0) {
                break;
            }

            if (buffer.Length + read > BufferLimit) {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return new ReadOnlyMemory<byte>(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
