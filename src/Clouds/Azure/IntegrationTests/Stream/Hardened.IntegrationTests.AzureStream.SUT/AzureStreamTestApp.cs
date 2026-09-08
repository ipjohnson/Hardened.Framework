using Hardened.Shared.Runtime.Attributes;

namespace Hardened.IntegrationTests.AzureStream.SUT;

/// <summary>
/// The entry point a stream function is anchored on.
/// </summary>
/// <remarks>
/// No Event Hubs module attribute, the same as every other fixture here: <c>[Stream]</c> on a
/// handler is what pulls the adapter in, through the <c>HardenedStreamModule</c> property the
/// adapter package declares. Nothing in this file names Azure, so the same application moved to
/// Kinesis changes a package reference and nothing else - <c>StreamTestApp</c> is this file with
/// another name.
/// </remarks>
[HardenedModule]
public partial class AzureStreamTestApp {
}
