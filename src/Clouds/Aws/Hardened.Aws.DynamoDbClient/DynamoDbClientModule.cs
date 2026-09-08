using Hardened.Shared.Runtime.Attributes;

namespace Hardened.Aws.DynamoDbClient;

/// <summary>
/// Registers <see cref="IDynamoDbClientProvider"/>. Import it from an application module:
/// <code>
/// [HardenedModule]
/// [DynamoDbClientModule]
/// public partial class MyApp { }
/// </code>
/// </summary>
/// <remarks>
/// <b>Client in the name, because DynamoDB is two things here.</b> This supplies clients that read
/// and write a table; <c>Hardened.Aws.Lambda.DynamoDb</c>'s <c>[DynamoDbStreamsModule]</c> serves
/// that table's change feed. Both were <c>[DynamoDbModule]</c> until 0.31.0, so an application
/// doing both could not name either without qualifying it.
/// </remarks>
[HardenedModule]
public partial class DynamoDbClientModule { }
