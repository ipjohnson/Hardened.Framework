using Hardened.Amz.DynamoDbClient;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Attributes;

namespace Hardened.Amz.Canaries.Runtime;

[HardenedStartup]
public partial class CanaryLibrary
{
    private IEnumerable<IApplicationModule> Modules()
    {
        yield return new DynamoDbClientLibrary();
    }
}