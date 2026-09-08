using Hardened.Shared.Runtime.Attributes;

namespace Hardened.IntegrationTests.AzureBlob.SUT;

/// <summary>
/// The entry point a blob function is anchored on.
/// </summary>
/// <remarks>
/// No Blob Storage module attribute: <c>[Blob]</c> on a handler is what pulls the adapter in,
/// through the <c>HardenedBlobModule</c> property the adapter package declares. Nothing in this
/// file names Azure - <c>BlobTestApp</c> is this file with another name.
/// </remarks>
[HardenedModule]
public partial class AzureBlobTestApp {
}
