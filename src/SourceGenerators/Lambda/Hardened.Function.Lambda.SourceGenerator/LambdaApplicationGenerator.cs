using CSharpAuthor;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.Function.Lambda.SourceGenerator
{
    public class LambdaApplicationGenerator
    {
        public static void Setup(
            IncrementalGeneratorInitializationContext context,
            IncrementalValuesProvider<EntryPointSelector.Model> valuesProvider)
        {
            context.RegisterSourceOutput(
                valuesProvider,
                SourceGeneratorWrapper.Wrap<EntryPointSelector.Model>(WriteSourceFile)
            );

        }

        private static void WriteSourceFile(SourceProductionContext arg1, EntryPointSelector.Model arg2)
        {
            var writer = new LambdaApplicationEntryPointWriter();
            var csharpFile = new CSharpFileDefinition(arg2.EntryPointType.Namespace);

            writer.CreateApplicationClass(arg2, csharpFile);

            var outputContext = new OutputContext();
            csharpFile.WriteOutput(outputContext);

            arg1.AddSource(arg2.EntryPointType.Name + ".LambdaApplication.cs", outputContext.Output());
        }
    }
}
