using CSharpAuthor;
using static CSharpAuthor.SyntaxHelpers;
using Hardened.SourceGenerator.Models;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Web
{
    public class WebExecutionHandlerCodeGenerator
    {
        public void GenerateSource(SourceProductionContext sourceProductionContext, RequestHandlerModel requestHandlerModel)
        {
            var sourceFile = GenerateFile(requestHandlerModel);

            File.AppendAllText(@"C:\temp\invoker.cs", sourceFile);
            sourceProductionContext.AddSource(requestHandlerModel.InvokeHandlerType.Name, sourceFile);
        }
        
        public string GenerateFile(RequestHandlerModel requestHandlerModel)
        {
            var csharpFile = new CSharpFileDefinition(requestHandlerModel.InvokeHandlerType.Namespace);
            
            InvokeClassGenerator.GenerateInvokeClass(requestHandlerModel, csharpFile);

            var outputContext = new OutputContext();

            csharpFile.WriteOutput(outputContext);

            return outputContext.Output();
        }
        
    }
}
