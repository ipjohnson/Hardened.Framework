using Hardened.Functions.Runtime.Attributes;
using Hardened.Gcp.CloudRun.Runtime;
using Hardened.Shared.Runtime.Attributes;

namespace Hardened.IntegrationTests.CloudRunBlob.SUT;

/// <summary>
/// The entry point a blob service is anchored on. No Storage module attribute: <c>[Blob]</c> on
/// the handler is what pulls the envelope in.
/// </summary>
[HardenedModule]
[CloudRunRuntime]
public partial class CloudRunBlobApp {
}

/// <summary>
/// What Cloud Storage says about an object, which is all a notification carries. The object itself
/// is not in it and fetching it is the handler's own call.
/// </summary>
public class Upload {
    public string Bucket { get; set; } = "";

    public string Name { get; set; } = "";

    public long Size { get; set; }

    public string EventType { get; set; } = "";
}

/// <summary>Where a handled notification goes, so a test can observe it.</summary>
public interface IUploadSink {
    void Arrived(Upload upload);
}

public class UploadHandlers {
    [Blob("uploads")]
    public void OnUpload(Upload upload, IUploadSink sink) => sink.Arrived(upload);
}

/// <summary>A sink that reports every notification on the process's output, for the container tier.</summary>
public sealed class ObservedUploadSink : IUploadSink {
    public void Arrived(Upload upload) {
        Console.Out.WriteLine("HARDENED-OBSERVED " + System.Text.Json.JsonSerializer.Serialize(
            new { kind = "blob", bucket = upload.Bucket, name = upload.Name, size = upload.Size, eventType = upload.EventType }));
        Console.Out.Flush();
    }
}
