using System;
using System.Collections.Generic;
using System.Text;
using CSharpAuthor;
using Hardened.SourceGenerator.Shared;

namespace Hardened.Function.Lambda.SourceGenerator
{
    public class LambdaApplicationEntryPointWriter : ApplicationEntryPointFileWriter
    {
        protected override void CustomConstructorLogic(EntryPointSelector.Model entryPoint, ClassDefinition appClass, ConstructorDefinition constructor,
            ParameterDefinition environment)
        {
            var providerInstanceDefinition =
                appClass.Fields.First(f => f.Name == RootServiceProvider).Instance;

            var filterProvider =
                constructor.Assign(providerInstanceDefinition.InvokeGeneric("GetRequiredService",
                    new[] { KnownTypes.Lambda.ILambdaInvokeFilterProvider })).ToVar("filterProvider");

            constructor.Assign(filterProvider.Invoke("ProvideFilter", RootServiceProvider)).ToVar("handler");

            var middleware =
                constructor.Assign(providerInstanceDefinition.InvokeGeneric("GetRequiredService",
                    new[] { KnownTypes.Requests.IMiddlewareService })).ToVar("middleware");

            constructor.AddIndentedStatement(middleware.Invoke("Use", "_ => handler"));

            var lambdaFunctionImplField = appClass.AddField(KnownTypes.Lambda.ILambdaFunctionImplService,
                "_lambdaFunctionImplService");

            constructor.Assign(providerInstanceDefinition.InvokeGeneric("GetRequiredService",
                new[] { KnownTypes.Lambda.ILambdaFunctionImplService })).To(lambdaFunctionImplField.Instance);
        }

        protected override void CreateDomainMethods(EntryPointSelector.Model model, ClassDefinition classDefinition)
        {
            var invokeMethod = classDefinition.AddMethod("Invoke");
            invokeMethod.Modifiers = ComponentModifier.Public;
            invokeMethod.SetReturnType(TypeDefinition.Task(typeof(Stream)));

            var inputStream = invokeMethod.AddParameter(typeof(Stream), "inputStream");
            var lambdaContext = invokeMethod.AddParameter(KnownTypes.Lambda.ILambdaContext, "lambdaContext");

            var lambdaFunctionImplField = classDefinition.Fields.First(f => f.Name == "_lambdaFunctionImplService");
            
            IOutputComponent invokeStatement = 
                lambdaFunctionImplField.Instance.Invoke("InvokeFunction", inputStream, lambdaContext);

            invokeMethod.Return(invokeStatement);
        }
        
        protected override ITypeDefinition LoggerHelper => KnownTypes.Lambda.LambdaLoggerHelper;

        protected override IEnumerable<ITypeDefinition> RegisterDiTypes()
        {
            yield return KnownTypes.DI.Registry.RequestRuntimeDI;
            yield return KnownTypes.DI.Registry.LambdaFunctionRuntimeDI;
        }
    }
}