using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Web.Routing;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Web;

public class WebExecutionHandlerCodeGenerator {
    public void GenerateSource(SourceProductionContext sourceProductionContext,
        RequestHandlerModel requestHandlerModel) {
        GenerateSource(sourceProductionContext, requestHandlerModel, false);
    }

    public void GenerateSource(SourceProductionContext sourceProductionContext,
        RequestHandlerModel requestHandlerModel,
        IReadOnlyList<RouteConstraintModel> constraints) {
        GenerateSource(sourceProductionContext, requestHandlerModel, false, constraints);
    }

    public void GenerateSource(SourceProductionContext sourceProductionContext,
        RequestHandlerModel requestHandlerModel, bool excludeFromCoverage,
        IReadOnlyList<RouteConstraintModel>? constraints = null) {
        sourceProductionContext.CancellationToken.ThrowIfCancellationRequested();

        // A parameter whose type does not resolve cannot be bound, so this handler is skipped and
        // the reason reported. Reported here rather than in the routing table because this stage
        // runs once per handler; the routing table sees them all and would report each repeatedly.
        if (requestHandlerModel.ReportIfUnresolved(sourceProductionContext)) {
            return;
        }

        // A token written in a brace form Hardened does not compile - {id:int}, {id?}, {id=5} - is
        // an error but not a reason to stop emitting. The routing table filters on unresolved
        // parameters, not on token syntax, so skipping here would leave it routing to a handler
        // class that no longer exists: a pile of CS0246s on top of the one diagnostic that says
        // what is actually wrong. The build fails either way; this way it fails legibly.
        requestHandlerModel.ReportUnsupportedTokens(sourceProductionContext, constraints);

        ThrownResponseSelector.Report(
            sourceProductionContext,
            requestHandlerModel.ControllerType.Name + "." + requestHandlerModel.HandlerMethod,
            requestHandlerModel.ResponseInformation.ThrowsDiagnostic);

        // Same treatment as an unsupported token: an error, and emit anyway. The handler compiles
        // and routes correctly - one of its two readings of the body just comes back empty - so
        // skipping it would replace one legible diagnostic with a routing table pointing at a
        // class that was never written.
        FormAndBodyDiagnostics.Report(sourceProductionContext, requestHandlerModel);

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