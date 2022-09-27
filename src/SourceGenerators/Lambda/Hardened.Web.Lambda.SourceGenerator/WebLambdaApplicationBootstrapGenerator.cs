using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.Web.Lambda.SourceGenerator
{
    public static class WebLambdaApplicationBootstrapGenerator
    {
        public static void Setup(IncrementalGeneratorInitializationContext initializationContext,
            IncrementalValuesProvider<EntryPointSelector.Model> incrementalValuesProvider)
        {
            initializationContext.RegisterSourceOutput(
                incrementalValuesProvider,
                SourceGeneratorWrapper.Wrap<EntryPointSelector.Model>(ModelWriter)
                );
        }

        private static void ModelWriter(SourceProductionContext arg1, EntryPointSelector.Model entryPoint)
        {

            var applicationFile = ApplicationFileWriter.WriteFile(entryPoint);

            arg1.AddSource(entryPoint.EntryPointType.Name + ".App", applicationFile);
        }

    }
}
