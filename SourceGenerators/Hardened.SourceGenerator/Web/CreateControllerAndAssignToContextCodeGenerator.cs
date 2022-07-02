using CSharpAuthor;
using Hardened.SourceGenerator.Models;
using Hardened.SourceGenerator.Shared;

namespace Hardened.SourceGenerator.Web
{
    public static class CreateControllerAndAssignToContextCodeGenerator
    {
        public static void Implement(WebEndPointModel endPointModel, ClassDefinition classDefinition)
        {
            var createControllerMethod = classDefinition.AddMethod("CreateControllerAndAssignToContext");

            createControllerMethod.Modifiers = ComponentModifier.Protected | ComponentModifier.Override;
            var context = createControllerMethod.AddParameter(KnownTypes.Requests.IExecutionContext, "context");

            createControllerMethod.AddUsingNamespace("Microsoft.Extensions.DependencyInjection");

            var invoke = context.Property("RequestService")
                .InvokeGeneric("GetRequiredService", new[] { endPointModel.ControllerType });

            createControllerMethod.Assign(invoke).To(context.Property("HandlerInstance"));
            
            createControllerMethod.NewLine();

            createControllerMethod.Assign("HandlerInfo").To(context.Property("HandlerInfo"));
        }
    }
}
