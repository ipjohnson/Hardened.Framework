using Microsoft.CodeAnalysis;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Function;

namespace Hardened.Function.SourceGenerator;

[Generator]
public class FunctionLibrarySourceGenerator : IIncrementalGenerator {
    public void Initialize(IncrementalGeneratorInitializationContext context) {
        var applicationModel = context.SyntaxProvider.CreateSyntaxProvider(
            EntryPointSelector.UsingAttribute(),
            EntryPointSelector.TransformModel(false)
        ).WithComparer(new EntryPointSelector.Comparer());

        FunctionIncrementalGenerator.Setup(context, applicationModel);

        // Which payload adapter each trigger in the project needs, read from the runtime package's
        // own build properties rather than known here.
        TriggerModuleGenerator.Setup(context, applicationModel);
    }
}
