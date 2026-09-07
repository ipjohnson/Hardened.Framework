using Hardened.Shared.Runtime.Attributes;

namespace Hardened.IntegrationTests.Sqs.SUT;

/// <summary>
/// The entry point a queue function is anchored on.
/// </summary>
/// <remarks>
/// <b>There is no SQS module attribute here, and that is the point.</b> The adapter, its serializer
/// context and the batch fan-out filter all arrive because a handler in this project wrote
/// <c>[Queue]</c>: the generator reads <c>HardenedQueueModule</c> off the runtime package's build
/// properties and adds the module to <c>DependencyRegistry&lt;SqsTestApp&gt;</c>. Nothing in this
/// file names SQS, so the same application moved to another provider changes a package reference
/// and nothing else.
/// </remarks>
[HardenedModule]
public partial class SqsTestApp {
}
