using DependencyModules.Runtime.Attributes;
using Hardened.Amz.Function.Lambda.Runtime.DependencyInjection;

namespace Hardened.Amz.Function.Lambda.Streaming;

/// <summary>
/// Response streaming for Lambda functions, applied to an application as
/// <c>[StreamingLambdaFunctionModule]</c>.
///
/// <para>
/// Being a <c>[DependencyModule]</c> declared in this assembly is what registers the streaming
/// runtime: DependencyModules picks up the <c>[SingletonService]</c> types beside it. It carries
/// <c>[LambdaFunctionModule]</c> for the invocation path and request pipeline underneath. Applying
/// it is also what selects the streaming bootstrap, so there is one opt-in rather than a module and
/// a separate marker that have to agree.
/// </para>
/// <para>
/// The class was named <c>StreamingLambdaFunction</c> until 2026-08-27, which made the generated
/// attribute <c>[StreamingLambdaFunction]</c> - the only module in the repository that did not say
/// so in its name, against <c>[LambdaWebModule]</c>, <c>[LambdaFunctionModule]</c> and
/// <c>[StreamingLambdaWebModule]</c>.
/// </para>
/// </summary>
[DependencyModule]
[LambdaFunctionModule]
public partial class StreamingLambdaFunctionModule { }
