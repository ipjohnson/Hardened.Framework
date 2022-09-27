using System.Collections.Immutable;
using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.Function.Lambda.SourceGenerator
{
    public class LambdaFunctionInvokerFileWriter
    {
        public void GenerateSource(
            SourceProductionContext sourceContext, 
            (RequestHandlerModel entryModel, ImmutableArray<EntryPointSelector.Model> appModel) data)
        {
            sourceContext.CancellationToken.ThrowIfCancellationRequested();

            var entryModel = data.entryModel;
            var appModel = data.appModel.First();

            var generatedFile = GenerateFile(entryModel, appModel, sourceContext.CancellationToken);
            
            sourceContext.AddSource(entryModel.Name.Path + ".FunctionHandler.cs", generatedFile);
        }

        private string GenerateFile(RequestHandlerModel entryModel, EntryPointSelector.Model appModel,
            CancellationToken cancellationToken)
        {
            var csharpFile = new CSharpFileDefinition(entryModel.InvokeHandlerType.Namespace);

            InvokeClassGenerator.GenerateInvokeClass(entryModel, csharpFile, cancellationToken);

            var outputContext=  new OutputContext();

            csharpFile.WriteOutput(outputContext);

            return outputContext.Output();
        }
        
    }
}
