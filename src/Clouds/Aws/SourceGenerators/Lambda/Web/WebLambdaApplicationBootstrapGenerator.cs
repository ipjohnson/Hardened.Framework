using System.Collections.Immutable;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.Amz.Web.Lambda.SourceGenerator;

public static class WebLambdaApplicationBootstrapGenerator {
    public static void Setup(IncrementalGeneratorInitializationContext initializationContext,
        IncrementalValuesProvider<(EntryPointSelector.Model EntryPoint, ImmutableArray<string> StreamingHandlers)> incrementalValuesProvider) {
        initializationContext.RegisterSourceOutput(
            incrementalValuesProvider,
            SourceGeneratorWrapper.Wrap<(EntryPointSelector.Model EntryPoint, ImmutableArray<string> StreamingHandlers)>(ModelWriter)
        );
    }

    private static void ModelWriter(SourceProductionContext arg1,
        (EntryPointSelector.Model EntryPoint, ImmutableArray<string> StreamingHandlers) model) {
        var entryPoint = model.EntryPoint;

        if (SelectsRestApiIntegration(entryPoint)) {
            arg1.ReportDiagnostic(
                Diagnostic.Create(
                    WebLambdaDiagnostics.RestApiIntegrationNotSupported,
                    Location.None,
                    entryPoint.EntryPointType.Name));

            return;
        }

        var applicationFile = ApplicationFileWriter.WriteFile(entryPoint, model.StreamingHandlers);

        arg1.AddSource(entryPoint.EntryPointType.Name + ".App", applicationFile);
    }

    /// <summary>
    /// Whether the entry point asks for the REST API integration this generator cannot emit.
    ///
    /// <para>
    /// Read off <see cref="AttributeModel.PropertyAssignment"/>, which is the assignment list as
    /// written — <c>"Version = ProxyIntegrationType.ApiGateway"</c>. That is the only form the
    /// model carries; it holds no symbol for the argument. Matching on the member name is therefore
    /// as precise as this can be, and it is precise enough: <c>HttpApiV2</c>, the value that works,
    /// does not contain it.
    /// </para>
    /// </summary>
    private static bool SelectsRestApiIntegration(EntryPointSelector.Model entryPoint) {
        if (entryPoint.AttributeModels == null) {
            return false;
        }

        foreach (var attribute in entryPoint.AttributeModels) {
            if (!attribute.TypeDefinition.Name.StartsWith("LambdaWebApplication")) {
                continue;
            }

            if (attribute.PropertyAssignment?.Contains("ProxyIntegrationType.ApiGateway") == true) {
                return true;
            }
        }

        return false;
    }
}
