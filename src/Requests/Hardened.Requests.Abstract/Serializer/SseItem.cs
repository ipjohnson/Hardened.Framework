namespace Hardened.Requests.Abstract.Serializer;

/// <summary>
/// The event fields, without the payload's type.
/// </summary>
/// <remarks>
/// The framing runs against a response value it holds as <c>object</c> - a filter may have replaced
/// it - so it cannot pattern-match <c>SseItem&lt;T&gt;</c> without knowing <c>T</c>. This is what it
/// matches instead, and it is why <see cref="Data"/> is <c>object?</c> here and typed on the record.
/// </remarks>
public interface ISseEvent {
    /// <summary>The payload, which is what gets serialized as <c>data:</c>.</summary>
    object? Data { get; }

    /// <summary>The event's <c>id:</c>, or null.</summary>
    string? Id { get; }

    /// <summary>The event's <c>event:</c>, or null.</summary>
    string? Event { get; }

    /// <summary>The event's <c>retry:</c> in milliseconds, or null.</summary>
    int? Retry { get; }
}

/// <summary>
/// One server-sent event: a payload, and the fields the protocol lets you send beside it.
/// </summary>
/// <remarks>
/// <para>
/// Optional. A handler returning <c>IAsyncEnumerable&lt;T&gt;</c> under
/// <c>[ServerSentEvents]</c> sends each <c>T</c> as the <c>data:</c> of an otherwise bare event,
/// which is what most streams want. This is for the ones that need more.
/// </para>
/// <para>
/// <b><see cref="Id"/> is the one worth knowing about.</b> A browser <c>EventSource</c> remembers
/// the last id it saw and sends it back as <c>Last-Event-ID</c> when it reconnects - which it does
/// on its own, without the page asking. A stream that sets ids can resume; one that does not
/// replays from wherever the handler starts. That is a decision about the resource rather than
/// about formatting, which is why it is expressible at all.
/// </para>
/// </remarks>
/// <param name="Data">The payload, serialized as the event's <c>data:</c>.</param>
/// <param name="Id">
/// The event's <c>id:</c>. Sent back by a reconnecting client as <c>Last-Event-ID</c>.
/// </param>
/// <param name="Event">
/// The event's <c>event:</c>, which a client dispatches by name. Absent means <c>message</c>,
/// which is what <c>EventSource.onmessage</c> receives.
/// </param>
/// <param name="Retry">
/// How long a client should wait before reconnecting, in milliseconds. Sent as <c>retry:</c>, and
/// worth sending once rather than on every event.
/// </param>
public record SseItem<T>(T Data, string? Id = null, string? Event = null, int? Retry = null)
    : ISseEvent {

    /// <summary>
    /// The payload as the framing sees it, which does not know <typeparamref name="T"/>.
    /// </summary>
    object? ISseEvent.Data => Data;
}
