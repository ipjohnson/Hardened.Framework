using System;
using System.Collections.Generic;
using System.Text;
using CSharpAuthor;
using static CSharpAuthor.SyntaxHelpers;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;

namespace Hardened.SourceGenerator.Requests
{
    public static class BindRequestParametersMethodGenerator
    {
        public static void Implement(RequestHandlerModel requestHandlerModel, ClassDefinition classDefinition)
        {
            var invokeMethod = classDefinition.AddMethod("BindRequestParameters");

            invokeMethod.Modifiers = ComponentModifier.Private | ComponentModifier.Static;

            invokeMethod.Modifiers |= ComponentModifier.Async;
            invokeMethod.SetReturnType(new GenericTypeDefinition(typeof(Task<>), new[] { KnownTypes.Requests.IExecutionRequestParameters }));

            var context = invokeMethod.AddParameter(KnownTypes.Requests.IExecutionContext, "context");

            ProcessParameters(requestHandlerModel, classDefinition, invokeMethod, context);
        }

        private static void ProcessParameters(RequestHandlerModel requestHandlerModel, ClassDefinition classDefinition, MethodDefinition invokeMethod, ParameterDefinition context)
        {
            var parametersVar = invokeMethod.Assign(New(InvokeClassGenerator.GenericParameters)).ToVar("parameters");

            foreach (var parameterInformation in requestHandlerModel.RequestParameterInformationList)
            {
                switch (parameterInformation.BindingType)
                {
                    case ParameterBindType.Body:
                        BindBodyParameter(parameterInformation, invokeMethod, context, parametersVar);
                        break;

                    case ParameterBindType.Header:
                        BindHeaderParameter(parameterInformation, invokeMethod, context, parametersVar);
                        break;

                    default:
                        throw new NotImplementedException("Not supported yet");
                }
            }

            invokeMethod.Return(parametersVar);
        }

        private static void BindHeaderParameter(RequestParameterInformation parameterInformation, MethodDefinition invokeMethod, ParameterDefinition context, InstanceDefinition parametersVar)
        {
            var bindingName = parameterInformation.BindingName;

            if (string.IsNullOrEmpty(bindingName))
            {
                bindingName = parameterInformation.Name;
            }

            var header = context.Property("Request").Property("Headers").Invoke("Get", QuoteString(bindingName));

            var stringInvokeStatement = context.Property("KnownServices").Property("StringConverterService");

            IOutputComponent? invokeStatement;
            if (parameterInformation.Required)
            {
                invokeStatement = 
                    stringInvokeStatement.InvokeGeneric("ParseRequired", new[] { parameterInformation.ParameterType },
                        header, QuoteString(bindingName));
            }
            else
            {
                invokeStatement =
                    stringInvokeStatement.InvokeGeneric("ParseOptional", new[] { parameterInformation.ParameterType },
                        header, QuoteString(bindingName));
            }

            invokeMethod.Assign(invokeStatement).To(parametersVar.Property(parameterInformation.Name));
        }

        private static void BindBodyParameter(RequestParameterInformation parameterInformation, MethodDefinition invokeMethod, ParameterDefinition context, InstanceDefinition parametersVar)
        {
            var getRequiredService = context.Property("KnownServices").Property("ContextSerializationService");

            getRequiredService.AddUsingNamespace(KnownTypes.Namespace.Microsoft.Extensions.DependencyInjection);

            var contentSerializationService =
                invokeMethod.Assign(getRequiredService).ToVar("contentSerializationService");

            var deserializeStatement = Await(contentSerializationService.InvokeGeneric("DeserializeRequestBody",
                new[] { parameterInformation.ParameterType }, context));

            invokeMethod.Assign(deserializeStatement).To(parametersVar.Property(parameterInformation.Name));
        }
    }
}
