using Hardened.Functions.Runtime.Attributes;

namespace Hardened.IntegrationTests.S3.SUT;

/// <summary>
/// What S3 says about an object, which is all a notification carries.
/// </summary>
/// <remarks>
/// A handler declares this the way it declares a queue message's type. The object itself is not in
/// the notification and fetching it is the handler's own call - which is the one way a blob trigger
/// differs from every other trigger in the framework.
/// </remarks>
public class Upload {
    public string Bucket { get; set; } = "";

    public string Key { get; set; } = "";

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
