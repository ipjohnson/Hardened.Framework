using Hardened.Functions.Runtime.Attributes;

namespace Hardened.IntegrationTests.AzureBlob.SUT;

/// <summary>
/// What Storage says about a blob, which is all a notification carries.
/// </summary>
/// <remarks>
/// A handler declares this the way it declares a queue message's type. The blob itself is not in
/// the notification and fetching it is the handler's own call - which is the one way a blob
/// trigger differs from every other trigger in the framework. The S3 fixture's <c>Upload</c> with
/// a container and a name where S3 has a bucket and a key, because that is what each store calls
/// them.
/// </remarks>
public class Upload {
    public string Container { get; set; } = "";

    public string Name { get; set; } = "";

    public long Size { get; set; }

    public string EventName { get; set; } = "";
}

/// <summary>Where a handled notification goes, so a test can observe it.</summary>
public interface IUploadSink {
    void Arrived(Upload upload);
}

public class UploadHandlers {
    [Blob("uploads")]
    public void OnUpload(Upload upload, IUploadSink sink) => sink.Arrived(upload);
}
