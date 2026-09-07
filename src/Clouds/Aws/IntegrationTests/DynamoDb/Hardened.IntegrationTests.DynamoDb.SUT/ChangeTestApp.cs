using Hardened.Shared.Runtime.Attributes;

namespace Hardened.IntegrationTests.DynamoDb.SUT;

/// <summary>
/// The entry point a change feed function is anchored on.
/// </summary>
/// <remarks>
/// <b>There is no DynamoDB module attribute here, and that is the point.</b> The adapter, its
/// serializer context and the batch fan-out filter all arrive because a handler in this project
/// wrote <c>[Change]</c>: the generator reads <c>HardenedChangeModule</c> off the adapter package's
/// build properties and adds the module to <c>DependencyRegistry&lt;ChangeTestApp&gt;</c>. Nothing
/// in this file names DynamoDB, so the same application moved to a Cosmos change feed changes a
/// package reference and nothing else.
/// </remarks>
[HardenedModule]
public partial class ChangeTestApp {
}
