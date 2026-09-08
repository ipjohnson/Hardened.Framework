using DependencyModules.Testing.Attributes;
using Hardened.IntegrationTests.AzureBlob.SUT;
using Hardened.Shared.Testing.Attributes;
using NSubstitute;
using Xunit;

namespace Hardened.IntegrationTests.AzureBlob.SUT.Tests;

/// <summary>
/// A blob function, whole: the trigger attribute, the generators, the module the build property
/// named, the adapter, the notification it projects and the invocation handler.
///
/// <para>
/// The S3 fixture's test file with Storage's names for things - a container and a name where S3
/// has a bucket and a key. It is compiled into two test projects, the pipeline rung and the
/// worker rung, and passes in both.
/// </para>
/// </summary>
public class BlobTests {

    [HardenedTest]
    public async Task ANotificationReachesTheHandler(
        AzureBlobTestApp.Blobs blobs, [Mock] IUploadSink sink) {
        await blobs.Uploads(new Upload { Name = "report.pdf", Size = 1024 });

        sink.Received().Arrived(Arg.Is<Upload>(
            upload => upload.Name == "report.pdf" && upload.Size == 1024));
    }

    /// <summary>
    /// The name arrives decoded. The worker rung carries it through a blob URI, where a space is
    /// percent-encoded, and a handler seeing the encoding would fetch a blob that does not exist.
    /// </summary>
    [HardenedTest]
    public async Task ANameWithASpaceArrivesDecoded(
        AzureBlobTestApp.Blobs blobs, [Mock] IUploadSink sink) {
        await blobs.Uploads(new Upload { Name = "my report.pdf" });

        sink.Received().Arrived(Arg.Is<Upload>(upload => upload.Name == "my report.pdf"));
    }

    /// <summary>
    /// The container the notification names reaches the handler. Through the pipeline the
    /// notification is the message as written; through the worker it is what the adapter reads
    /// off the blob client, which is bound to the container the trigger declared.
    /// </summary>
    [HardenedTest]
    public async Task TheContainerComesFromTheNotification(
        AzureBlobTestApp.Blobs blobs, [Mock] IUploadSink sink) {
        await blobs.Uploads(new Upload { Container = "uploads", Name = "a.txt" });

        sink.Received().Arrived(Arg.Is<Upload>(upload => upload.Container == "uploads"));
    }

    /// <summary>
    /// Every blob is handled on its own: through the pipeline as one batch forked per message,
    /// through the worker as one invocation per blob, which is how the trigger fires.
    /// </summary>
    [HardenedTest]
    public async Task EveryNotificationInABatchIsHandledSeparately(
        AzureBlobTestApp.Blobs blobs, [Mock] IUploadSink sink) {
        await blobs.Uploads(
            new Upload { Name = "a.txt" }, new Upload { Name = "b.txt" }, new Upload { Name = "c.txt" });

        sink.Received(3).Arrived(Arg.Any<Upload>());
        sink.Received().Arrived(Arg.Is<Upload>(upload => upload.Name == "b.txt"));
    }

    /// <summary>
    /// Storage reads no response, so a failed notification has to fail the invocation.
    /// </summary>
    [HardenedTest]
    public async Task AFailedNotificationFailsTheInvocation(
        AzureBlobTestApp.Blobs blobs, [Mock] IUploadSink sink) {
        sink.When(one => one.Arrived(Arg.Is<Upload>(upload => upload.Name == "b.txt")))
            .Do(_ => throw new InvalidOperationException("refused"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => blobs.Uploads(new Upload { Name = "a.txt" }, new Upload { Name = "b.txt" }));
    }
}
