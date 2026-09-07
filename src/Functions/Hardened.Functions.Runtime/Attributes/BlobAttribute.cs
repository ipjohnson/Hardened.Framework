namespace Hardened.Functions.Runtime.Attributes;

/// <summary>
/// Routes changes to objects in a store to the attributed handler.
/// </summary>
/// <remarks>
/// <para>
/// S3 on AWS, Blob Storage on Azure, Cloud Storage on Google. The handler names the bucket and
/// nothing else; which adapter delivers to it is decided by the runtime package the project
/// references, through the <c>HardenedBlobModule</c> build property that package declares.
/// </para>
/// <para>
/// <b>The notification is the message, and the object is not in it.</b> Every other trigger hands a
/// handler something a publisher wrote; this one hands it a bucket, a key, a size and an event name,
/// and getting the object is a call the handler makes for itself. That is the transport's design
/// rather than the framework's, and it is the reason a blob handler binds metadata where a queue
/// handler binds a payload.
/// </para>
/// <para>
/// Routes as <c>BLOB /uploads</c>. Unordered and independent, unlike <see cref="StreamAttribute"/>
/// and <see cref="ChangeAttribute"/>: two notifications for one key can arrive out of order, and
/// the sequencer on each is what compares them.
/// </para>
/// </remarks>
public class BlobAttribute : Attribute {
    public BlobAttribute(string name) {
        Name = name;
    }

    /// <summary>
    /// The bucket's own name, not an ARN and not a URL.
    /// </summary>
    /// <remarks>
    /// A bucket name is globally unique on S3 and is the whole of its identity on every provider
    /// worth naming, which is what makes it the part a handler declares.
    /// </remarks>
    public string Name { get; }
}
