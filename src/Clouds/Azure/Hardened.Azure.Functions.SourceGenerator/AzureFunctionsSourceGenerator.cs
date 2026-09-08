using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.Azure.Functions.SourceGenerator;

/// <summary>
/// The Azure Functions worker's view of a Hardened application: a <c>[Function]</c> per trigger
/// handler, and the two seams the worker discovers and invokes them through.
/// </summary>
/// <remarks>
/// Referenced as an analyzer by the application, beside <c>Hardened.Function.SourceGenerator</c>,
/// which compiles the handlers, and <c>Hardened.Library.SourceGenerator</c>, which binds their
/// modules. This one reads the same model those two read and adds what only the isolated worker
/// needs; see <see cref="AzureFunctionsGenerator"/>.
/// </remarks>
[Generator]
public class AzureFunctionsSourceGenerator : IIncrementalGenerator {
    public void Initialize(IncrementalGeneratorInitializationContext context) {
        var applicationModel = context.SyntaxProvider.CreateSyntaxProvider(
            EntryPointSelector.UsingAttribute(),
            EntryPointSelector.TransformModel(false)
        ).WithComparer(new EntryPointSelector.Comparer());

        AzureFunctionsGenerator.Setup(context, applicationModel);
    }
}
