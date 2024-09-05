using Hardened.Amz.Function.Sqs.Runtime;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Shared.Runtime.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace SqsTest;

[HardenedModule]
[SqsLambda.Module]
public partial class Application {
}