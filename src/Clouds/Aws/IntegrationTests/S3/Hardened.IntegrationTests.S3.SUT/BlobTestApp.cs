using Hardened.Shared.Runtime.Attributes;

namespace Hardened.IntegrationTests.S3.SUT;

/// <summary>
/// The entry point a blob function is anchored on.
/// </summary>
/// <remarks>
/// No S3 module attribute: <c>[Blob]</c> on a handler is what pulls the adapter in, through the
/// <c>HardenedBlobModule</c> property the adapter package declares. Nothing in this file names S3.
/// </remarks>
[HardenedModule]
public partial class BlobTestApp {
}
