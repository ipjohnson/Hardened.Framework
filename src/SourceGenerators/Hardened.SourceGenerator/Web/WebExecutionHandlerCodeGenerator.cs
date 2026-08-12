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

        // A parameter whose type does not resolve cannot be bound, so this handler is skipped and
        // the reason reported. Reported here rather than in the routing table because this stage
        // runs once per handler; the routing table sees them all and would report each repeatedly.
        if (requestHandlerModel.ReportIfUnresolved(sourceProductionContext)) {
            return;
        }

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