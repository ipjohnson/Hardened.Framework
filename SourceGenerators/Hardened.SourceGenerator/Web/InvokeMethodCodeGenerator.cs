using CSharpAuthor;
using Hardened.SourceGenerator.Models;
using Hardened.SourceGenerator.Shared;
using static CSharpAuthor.SyntaxHelpers;

namespace Hardened.SourceGenerator.Web
{
    public static class InvokeMethodCodeGenerator
    {
        public static void Implement(WebEndPointModel endPointModel, ClassDefinition classDefinition)
        {
            var invokeMethod = classDefinition.AddMethod("InvokeMethod");

            invokeMethod.Modifiers = ComponentModifier.Private | ComponentModifier.Static;

            if (endPointModel.ResponseInformation.IsAsync)
            {
                invokeMethod.Modifiers |= ComponentModifier.Async;
                invokeMethod.SetReturnType(typeof(Task));
            }

            var context = invokeMethod.AddParameter(KnownTypes.Requests.IExecutionContext, "context");
            var controller = invokeMethod.AddParameter(endPointModel.ControllerType, "controller");
            
            InvokeDefinition invoke = controller.Invoke(endPointModel.HandlerMethod);

            ProcessArguments(endPointModel, invoke);

            IOutputComponent invokeStatement = invoke;

            if (endPointModel.ResponseInformation.IsAsync)
            {
                invokeStatement = Await(invokeStatement);
            }

            invokeMethod.Assign(invokeStatement).To(context.Property("Response.ResponseValue"));
        }

        private static void ProcessArguments(WebEndPointModel endPointModel, InvokeDefinition invoke)
        {
            
        }
    }
}
