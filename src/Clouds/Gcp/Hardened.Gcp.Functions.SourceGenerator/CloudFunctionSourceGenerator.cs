using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.Gcp.Functions.SourceGenerator;

/// <summary>
/// The Functions Framework's view of a Hardened application: the <c>FUNCTION_TARGET</c> type, and
/// the startup attribute that points it at the application.
/// </summary>
/// <remarks>
/// Referenced as an analyzer by the application, beside <c>Hardened.Function.SourceGenerator</c>,
/// which compiles the handlers, and <c>Hardened.Library.SourceGenerator</c>, which binds their
/// modules. It is gated by being referenced at all rather than by an MSBuild property: a project
/// that names this generator is deploying to Cloud Functions, and a project that does not gets
/// nothing. See <see cref="CloudFunctionEmitter"/> for why the type cannot live in a runtime
/// package.
/// </remarks>
[Generator]
public class CloudFunctionSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var applicationModel = context
            .SyntaxProvider.CreateSyntaxProvider(
                EntryPointSelector.UsingAttribute(),
                EntryPointSelector.TransformModel(false)
            )
            .WithComparer(new EntryPointSelector.Comparer());

        CloudFunctionGenerator.Setup(context, applicationModel);
    }
}
