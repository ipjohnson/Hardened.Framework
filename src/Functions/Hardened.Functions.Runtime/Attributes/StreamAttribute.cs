namespace Hardened.Functions.Runtime.Attributes;

/// <summary>
/// Routes records from a sharded stream to the attributed handler.
/// </summary>
/// <remarks>
/// <para>
/// Kinesis Data Streams on AWS, Event Hubs on Azure. The handler names the stream and nothing else;
/// which adapter delivers to it is decided by the runtime package the project references, through
/// the <c>HardenedStreamModule</c> build property that package declares.
/// </para>
/// <para>
/// <b>Not a queue, though both deliver a batch.</b> A queue's messages are independent and are
/// acknowledged one at a time; a stream's are ordered within a shard and acknowledged by position,
/// so a failure rewinds rather than singles one out. That difference is the whole of
/// <c>BatchFailureMode</c>, and it is why the same handler cannot be moved between the two by
/// changing the attribute alone.
/// </para>
/// <para>
/// Routes as <c>STREAM /clickstream</c>. A record's data is the publisher's own bytes, so the
/// handler binds its parameter from them the way a direct invocation does - the transport adds no
/// envelope the handler has to know about.
/// </para>
/// </remarks>
public class StreamAttribute : Attribute {
    public StreamAttribute(string name) {
        Name = name;
    }

    /// <summary>The stream's own name, not an ARN.</summary>
    public string Name { get; }
}
