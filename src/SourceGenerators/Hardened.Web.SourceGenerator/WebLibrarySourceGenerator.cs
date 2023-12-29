using Microsoft.CodeAnalysis;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Web;

namespace Hardened.Web.SourceGenerator;

[Generator]
public class WebLibrarySourceGenerator : IIncrementalGenerator {
    public void Initialize(IncrementalGeneratorInitializationContext context) {
        var applicationModel = context.SyntaxProvider.CreateSyntaxProvider(
            EntryPointSelector.UsingAttribute(),
            EntryPointSelector.TransformModel(false)
        ).WithComparer(new EntryPointSelector.Comparer());

        WebIncrementalGenerator.Setup(context, applicationModel);
    }
}