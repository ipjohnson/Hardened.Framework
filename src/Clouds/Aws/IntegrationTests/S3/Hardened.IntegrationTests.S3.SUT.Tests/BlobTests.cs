using DependencyModules.Testing.Attributes;
using Hardened.IntegrationTests.S3.SUT;
using Hardened.Shared.Testing.Attributes;
using NSubstitute;
using Xunit;

namespace Hardened.IntegrationTests.S3.SUT.Tests;

/// <summary>
/// A blob function, whole: the trigger attribute, the generator, the module the build property
/// named, the adapter, the key decode and the invocation loop.
/// </summary>
public class BlobTests {

    [HardenedTest]
    public async Task ANotificationReachesTheHandler(
        BlobTestApp.Blobs blobs, [Mock] IUploadSink sink) {
        await blobs.Uploads(new Upload { Key = "report.pdf", Size = 1024 });

        sink.Received().Arrived(Arg.Is<Upload>(
            upload => upload.Key == "report.pdf" && upload.Size == 1024));
    }

    /// <summary>
    /// The key arrives decoded, which is the thing this adapter exists to get right.
    /// </summary>
    /// <remarks>
    /// The delivery encodes the key the way S3 does, so a space is a plus on the wire. A handler
    /// seeing the plus would fetch an object that does not exist.
    /// </remarks>
    [HardenedTest]
    public async Task AKeyWithASpaceArrivesDecoded(
        BlobTestApp.Blobs blobs, [Mock] IUploadSink sink) {
        await blobs.Uploads(new Upload { Key = "my report.pdf" });

        sink.Received().Arrived(Arg.Is<Upload>(upload => upload.Key == "my report.pdf"));
    }

    /// <summary>
    /// The bucket the notification names reaches the handler, not the one the attribute declared.
    /// </summary>
    [HardenedTest]
    public async Task TheBucketComesFromTheNotification(
        BlobTestApp.Blobs blobs, [Mock] IUploadSink sink) {
        await blobs.Uploads(new Upload { Key = "a.txt" });

        sink.Received().Arrived(Arg.Is<Upload>(upload => upload.Bucket == "uploads"));
    }

    [HardenedTest]
    public async Task EveryNotificationInABatchIsHandledSeparately(
        BlobTestApp.Blobs blobs, [Mock] IUploadSink sink) {
        await blobs.Uploads(
            new Upload { Key = "a.txt" }, new Upload { Key = "b.txt" }, new Upload { Key = "c.txt" });

        sink.Received(3).Arrived(Arg.Any<Upload>());
        sink.Received().Arrived(Arg.Is<Upload>(upload => upload.Key == "b.txt"));
    }

    /// <summary>
    /// S3 reads no response, so a failed notification has to fail the invocation.
    /// </summary>
    [HardenedTest]
    public async Task AFailedNotificationFailsTheInvocation(
        BlobTestApp.Blobs blobs, [Mock] IUploadSink sink) {
        sink.When(one => one.Arrived(Arg.Is<Upload>(upload => upload.Key == "b.txt")))
            .Do(_ => throw new InvalidOperationException("refused"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => blobs.Uploads(new Upload { Key = "a.txt" }, new Upload { Key = "b.txt" }));
    }
}
