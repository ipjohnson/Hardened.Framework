using Hardened.Shared.Runtime.Attributes;

namespace Hardened.IntegrationTests.Kinesis.SUT;

/// <summary>
/// The entry point a stream function is anchored on.
/// </summary>
/// <remarks>
/// No Kinesis module attribute, the same as every other fixture here: <c>[Stream]</c> on a handler
/// is what pulls the adapter in, through the <c>HardenedStreamModule</c> property the adapter
/// package declares. Nothing in this file names Kinesis, so the same application moved to Event
/// Hubs changes a package reference and nothing else.
/// </remarks>
[HardenedModule]
public partial class StreamTestApp {
}
