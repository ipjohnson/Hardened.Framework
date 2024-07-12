using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Requests;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Web;

public class WebExecutionHandlerCodeGenerator
{
    public void GenerateSource(SourceProductionContext sourceProductionContext, RequestHandlerModel requestHandlerModel)
    {
        sourceProductionContext.CancellationToken.ThrowIfCancellationRequested();

        var sourceFile = GenerateFile(requestHandlerModel, sourceProductionContext.CancellationToken);

        sourceProductionContext.AddSource(requestHandlerModel.InvokeHandlerType.Name, sourceFile);
    }
        
    public string GenerateFile(RequestHandlerModel requestHandlerModel, CancellationToken cancellationToken)
    {
        var csharpFile = new CSharpFileDefinition(requestHandlerModel.InvokeHandlerType.Namespace);
            
        InvokeClassGenerator.GenerateInvokeClass(requestHandlerModel, csharpFile, cancellationToken);

        var outputContext = new OutputContext();

        csharpFile.WriteOutput(outputContext);

        return outputContext.Output();
    }
        
}