using CSharpAuthor;
using static CSharpAuthor.SyntaxHelpers;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;

namespace Hardened.SourceGenerator.Requests
{
    public static class InvokeMethodCodeGenerator
    {
        public static void Implement(RequestHandlerModel requestHandlerModel, ClassDefinition classDefinition)
        {
            var invokeMethod = classDefinition.AddMethod("InvokeMethod");

            invokeMethod.Modifiers = ComponentModifier.Private | ComponentModifier.Static;

            if (requestHandlerModel.ResponseInformation.IsAsync)
            {
                invokeMethod.Modifiers |= ComponentModifier.Async;
                invokeMethod.SetReturnType(typeof(Task));
            }

            var context = invokeMethod.AddParameter(KnownTypes.Requests.IExecutionContext, "context");
            var controller = invokeMethod.AddParameter(requestHandlerModel.ControllerType, "controller");
            
            InvokeDefinition invoke = controller.Invoke(requestHandlerModel.HandlerMethod);

            ProcessArguments(requestHandlerModel, invoke, invokeMethod);

            IOutputComponent invokeStatement = invoke;

            if (requestHandlerModel.ResponseInformation.IsAsync)
            {
                invokeStatement = Await(invokeStatement);
            }

            invokeMethod.Assign(invokeStatement).To(context.Property("Response.ResponseValue"));
        }

        private static void ProcessArguments(RequestHandlerModel requestHandlerModel, InvokeDefinition invoke,
            MethodDefinition invokeMethod)
        {
            if (requestHandlerModel.RequestParameterInformationList.Count > 0)
            {
                var parameters = invokeMethod.AddParameter(InvokeClassGenerator.GenericParameters, "parameters");

                foreach (var parameterInformation in requestHandlerModel.RequestParameterInformationList)
                {
                    invoke.AddArgument(parameters.Property(parameterInformation.Name));
                }
            }
        }
    }
}
