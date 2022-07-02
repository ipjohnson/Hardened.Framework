using CSharpAuthor;
using static CSharpAuthor.SyntaxHelpers;
using Hardened.SourceGenerator.Models;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Web
{
    public class WebExecutionHandlerCodeGenerator
    {
        public void GenerateSource(SourceProductionContext sourceProductionContext, WebEndPointModel endPointModel)
        {
            var sourceFile = GenerateFile(endPointModel);

            File.AppendAllText(@"C:\temp\invoker.cs", sourceFile);
            sourceProductionContext.AddSource(endPointModel.HandlerType.Name, sourceFile);
        }
        
        public string GenerateFile(WebEndPointModel endPointModel)
        {
            var csharpFile = new CSharpFileDefinition(endPointModel.HandlerType.Namespace);

            var classDefinition = csharpFile.AddClass(endPointModel.HandlerType.Name);

            ImplementInvokerClass(endPointModel, classDefinition);

            var outputContext = new OutputContext();

            csharpFile.WriteOutput(outputContext);

            return outputContext.Output();
        }

        private void ImplementInvokerClass(
            WebEndPointModel endPointModel, 
            ClassDefinition classDefinition)
        {
            var baseType = new GenericTypeDefinition(
                TypeDefinitionEnum.ClassDefinition,
                "BaseExecutionHandler",
                KnownTypes.Namespace.HardenedRequestsRuntimeExecution,
                new []{ endPointModel.ControllerType }
            );

            classDefinition.AddBaseType(baseType);

            CreateConstructor(endPointModel, classDefinition);

            //GetFilterListCodeGenerator.Implement(endPointModel, classDefinition);

            HandlerInfoCodeGenerator.Implement(endPointModel, classDefinition);
            
            InvokeMethodCodeGenerator.Implement(endPointModel, classDefinition);
        }
        
        private void CreateConstructor(WebEndPointModel webEndPointModel, ClassDefinition classDefinition)
        {
            var templateName = webEndPointModel.ResponseInformation.TemplateName;

            var defaultOutput = string.IsNullOrEmpty(templateName)
                ? Null()
                : Invoke(KnownTypes.Templates.DefaultOutputFuncHelper, "GetTemplateOut",
                    "serviceProvider", QuoteString(templateName!));

            var filterMethod = InvokeGeneric(
                KnownTypes.Requests.ExecutionHelper,
                "StandardFilterEmptyParameters",
                new[] { webEndPointModel.ControllerType },
                "serviceProvider",
                "InvokeMethod");
            var constructor = classDefinition.AddConstructor(Base(filterMethod, defaultOutput));

            var serviceProvider = 
                constructor.AddParameter(typeof(IServiceProvider), "serviceProvider");
        }
    }
}
