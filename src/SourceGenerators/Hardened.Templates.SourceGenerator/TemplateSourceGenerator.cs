using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Templates.Generator;
using Microsoft.CodeAnalysis;

namespace Hardened.Templates.SourceGenerator;

[Generator]
public class TemplateSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var applicationModel = context.SyntaxProvider.CreateSyntaxProvider(
            EntryPointSelector.UsingAttribute(),
            EntryPointSelector.TransformModel(false)
        ).WithComparer(new EntryPointSelector.Comparer());

        TemplateIncrementalGenerator.Setup(context, applicationModel, new[] { "html" });
    }
}