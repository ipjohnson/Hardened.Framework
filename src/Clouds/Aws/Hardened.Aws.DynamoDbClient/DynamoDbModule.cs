using Hardened.Shared.Runtime.Attributes;

namespace Hardened.Aws.DynamoDbClient;

/// <summary>
/// Registers <see cref="IDynamoDbClientProvider"/>. Import it from an application module:
/// <code>
/// [HardenedModule]
/// [DynamoDbModule]
/// public partial class MyApp { }
/// </code>
/// </summary>
[HardenedModule]
public partial class DynamoDbModule { }
