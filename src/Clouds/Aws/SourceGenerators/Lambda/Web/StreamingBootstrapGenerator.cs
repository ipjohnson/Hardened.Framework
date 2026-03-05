using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.Amz.Web.Lambda.SourceGenerator;

public static class StreamingBootstrapGenerator {
    public static void Setup(IncrementalGeneratorInitializationContext initializationContext,
        IncrementalValuesProvider<EntryPointSelector.Model> incrementalValuesProvider) {
        initializationContext.RegisterSourceOutput(
            incrementalValuesProvider,
            SourceGeneratorWrapper.Wrap<EntryPointSelector.Model>(ModelWriter)
        );
    }

    private static void ModelWriter(SourceProductionContext arg1, EntryPointSelector.Model entryPoint) {
        var applicationFile = StreamingApplicationFileWriter.WriteFile(entryPoint);

        arg1.AddSource(entryPoint.EntryPointType.Name + ".StreamingApp", applicationFile);
    }
}
