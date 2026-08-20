using Hardened.Amz.Function.Lambda.Runtime.DependencyInjection;
#if (sqs)
using Hardened.Amz.Function.Sqs.Runtime;
#endif
using Hardened.Shared.Runtime.Attributes;

namespace Hardened1;

/// <summary>
/// The application module: which runtime this runs on, and which libraries come along.
/// </summary>
/// <remarks>
/// [LambdaFunctionModule] brings the Lambda invocation path and, through the request module it
/// carries, the filter pipeline the generated entry point runs a payload through.
#if (sqs)
///
/// [SqsLambda] adds SQS batch handling on top: the runtime unpacks the batch, runs the handler
/// once per record, and reports the ones that threw as batch item failures so only those are
/// redelivered.
#endif
///
/// partial is not optional - the generator writes the other half, including the entry point AWS
/// invokes.
/// </remarks>
[HardenedModule]
[LambdaFunctionModule]
#if (sqs)
[SqsLambda]
#endif
public partial class Application;
