using Hardened.Functions.Runtime.Attributes;

namespace Hardened.IntegrationTests.Kinesis.SUT;

public class Click {
    public string Id { get; set; } = "";

    public int Count { get; set; }
}

/// <summary>Where a handled record goes, so a test can observe it.</summary>
public interface IClickSink {
    void Record(Click click);
}

public class ClickHandlers {
    /// <summary>
    /// A record, bound from the publisher's own bytes.
    /// </summary>
    /// <remarks>
    /// The claim the adapter rests on. Kinesis carried base64 of this object and said nothing about
    /// what was in it, so the handler declares <c>Click</c> and the transport adds no envelope it
    /// has to know about - the same arrangement a direct invocation has.
    /// </remarks>
    [Stream("clickstream")]
    public void OnClick(Click click, IClickSink sink) => sink.Record(click);
}
