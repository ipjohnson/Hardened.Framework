using DependencyModules.Testing.Attributes;
using Hardened.Functions.Testing;
using Hardened.IntegrationTests.CloudRunBlob.SUT;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Kestrel.Runtime;
using NSubstitute;
using Xunit;

namespace Hardened.IntegrationTests.CloudRunBlob.SUT.Tests;

/// <summary>
/// A blob service, whole: the trigger attribute, the generator, the module the build property
/// named, the Storage envelope reading the Eventarc form, the front door and the one dispatch. The
/// same tests the S3 fixture holds, on the pipeline host.
/// </summary>
public class BlobTests {

    [HardenedTest]
    public async Task ANotificationReachesTheHandler(CloudRunBlobApp.Blobs blobs, [Mock] IUploadSink sink) {
        await blobs.Uploads(new Upload { Name = "report.pdf", Size = 1024 });

        sink.Received().Arrived(Arg.Is<Upload>(upload => upload.Name == "report.pdf" && upload.Size == 1024));
    }

    /// <summary>Storage writes the object's name as it is, so a space arrives as a space with nothing to decode.</summary>
    [HardenedTest]
    public async Task ANameWithASpaceArrivesAsWritten(CloudRunBlobApp.Blobs blobs, [Mock] IUploadSink sink) {
        await blobs.Uploads(new Upload { Name = "my report.pdf" });

        sink.Received().Arrived(Arg.Is<Upload>(upload => upload.Name == "my report.pdf"));
    }

    /// <summary>The bucket the delivery names reaches the handler, and the event is named the notification's way.</summary>
    [HardenedTest]
    public async Task TheBucketAndTheEventComeFromTheDelivery(CloudRunBlobApp.Blobs blobs, [Mock] IUploadSink sink) {
        await blobs.Uploads(new Upload { Name = "a.txt" });

        sink.Received().Arrived(Arg.Is<Upload>(upload => upload.Bucket == "uploads" && upload.EventType == "OBJECT_FINALIZE"));
    }

    [HardenedTest]
    public async Task EveryNotificationIsHandledSeparately(CloudRunBlobApp.Blobs blobs, [Mock] IUploadSink sink) {
        await blobs.Uploads(new Upload { Name = "a.txt" }, new Upload { Name = "b.txt" }, new Upload { Name = "c.txt" });

        sink.Received(3).Arrived(Arg.Any<Upload>());
        sink.Received().Arrived(Arg.Is<Upload>(upload => upload.Name == "b.txt"));
    }

    [HardenedTest]
    public async Task AFailedNotificationIsNotAcknowledged(CloudRunBlobApp.Blobs blobs, [Mock] IUploadSink sink) {
        sink.When(one => one.Arrived(Arg.Is<Upload>(upload => upload.Name == "b.txt")))
            .Do(_ => throw new InvalidOperationException("refused"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => blobs.Uploads(new Upload { Name = "a.txt" }, new Upload { Name = "b.txt" }));
    }
}

/// <summary>The same notifications over a Kestrel socket.</summary>
[KestrelRuntime]
public class BlobOverASocketTests {

    [HardenedTest]
    public async Task ANotificationReachesTheHandler(CloudRunBlobApp.Blobs blobs, [Mock] IUploadSink sink) {
        await blobs.Uploads(new Upload { Name = "socket.pdf", Size = 2048 });

        sink.Received().Arrived(Arg.Is<Upload>(upload => upload.Name == "socket.pdf" && upload.Size == 2048 && upload.Bucket == "uploads"));
    }

    [HardenedTest]
    public async Task AFailedNotificationIsNotAcknowledged(CloudRunBlobApp.Blobs blobs, [Mock] IUploadSink sink) {
        sink.When(one => one.Arrived(Arg.Any<Upload>())).Do(_ => throw new InvalidOperationException("refused"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => blobs.Uploads(new Upload { Name = "a.txt" }));
    }
}

/// <summary>The same handler through the neutral delivery, which names no cloud.</summary>
[PipelineDelivery]
public class PipelineBlobTests {

    [HardenedTest]
    public async Task ANotificationReachesTheHandlerThroughThePipeline(CloudRunBlobApp.Blobs blobs, [Mock] IUploadSink sink) {
        await blobs.Uploads(new Upload { Name = "pipeline.pdf", Size = 7 });

        sink.Received().Arrived(Arg.Is<Upload>(upload => upload.Name == "pipeline.pdf" && upload.Size == 7));
    }
}
