using Hardened.Amz.Function.DDB.Runtime;
using Hardened.Amz.Function.Lambda.Runtime.DependencyInjection;
using Hardened.Shared.Runtime.Attributes;

namespace DynamoDbStreamApp;

/// <summary>
/// A Lambda application that handles DynamoDB stream events.
///
/// <para>
/// Two modules, and both are needed. <c>[LambdaFunctionModule]</c> brings the Lambda invocation
/// path and, through the <c>[HardenedRequestModule]</c> it carries, the request pipeline.
/// <c>[DynamoStreamLambda]</c> adds the stream record handling on top.
/// </para>
///
/// <para>
/// This was <c>[DynamoStreamLambda.Module]</c> before the DependencyModules upgrade. The nested
/// attribute no longer exists — the generator emits a top-level
/// <c>DynamoStreamLambdaAttribute</c> — which is why the harness stopped compiling and was left
/// unrestored. Restored 2026-08-12.
/// </para>
/// </summary>
[HardenedModule]
[LambdaFunctionModule]
[DynamoStreamLambda]
public partial class Application {
}
