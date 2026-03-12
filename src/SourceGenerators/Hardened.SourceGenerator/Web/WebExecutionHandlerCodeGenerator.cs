using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Requests;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Web;

public class WebExecutionHandlerCodeGenerator {
    public void GenerateSource(SourceProductionContext sourceProductionContext,
        RequestHandlerModel requestHandlerModel) {
        GenerateSource(sourceProductionContext, requestHandlerModel, false);
    }

    public void GenerateSource(SourceProductionContext sourceProductionContext,
        RequestHandlerModel requestHandlerModel, bool excludeFromCoverage) {
        sourceProductionContext.CancellationToken.ThrowIfCancellationRequested();

        var sourceFile = GenerateFile(requestHandlerModel, sourceProductionContext.CancellationToken, excludeFromCoverage);

        sourceProductionContext.AddSource(requestHandlerModel.InvokeHandlerType.Name, sourceFile);
    }

    public string GenerateFile(RequestHandlerModel requestHandlerModel, CancellationToken cancellationToken, bool excludeFromCoverage = false) {
        var csharpFile = new CSharpFileDefinition(requestHandlerModel.InvokeHandlerType.Namespace);

        InvokeClassGenerator.GenerateInvokeClass(requestHandlerModel, csharpFile, cancellationToken, excludeFromCoverage);

        var outputContext = new OutputContext(
            new OutputContextOptions {
                TypeOutputMode = TypeOutputMode.Global
            });

        csharpFile.WriteOutput(outputContext);

        return outputContext.Output();
    }
}