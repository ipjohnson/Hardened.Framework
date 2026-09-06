using Hardened.Amz.Function.Lambda.Runtime.DependencyInjection;
using Hardened.Amz.Function.Sqs.Runtime;
using Hardened.Shared.Runtime.Attributes;

namespace SqsTest;

/// <summary>
/// A Lambda application that handles SQS batches.
///
/// <para>
/// Two modules, and both are needed. <c>[LambdaFunctionModule]</c> brings the Lambda invocation
/// path and, through the <c>[HardenedRequestModule]</c> it carries, the request pipeline —
/// <c>IGlobalFilterRegistry</c> and <c>ILambdaInvokeFilterProvider</c> live there, and the
/// generated constructor resolves both. <c>[SqsLambda]</c> adds the SQS batch handling on top.
/// </para>
///
/// <para>
/// This was <c>[SqsLambda.Module]</c> before the DependencyModules upgrade. The nested attribute
/// no longer exists — the generator emits a top-level <c>SqsLambdaAttribute</c> — which is why the
/// harness stopped compiling and was left unrestored. Restored 2026-08-12.
/// </para>
/// </summary>
[HardenedModule]
[LambdaFunctionModule]
[SqsLambda]
public partial class Application {
}
