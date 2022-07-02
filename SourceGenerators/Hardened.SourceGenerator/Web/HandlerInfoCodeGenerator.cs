using CSharpAuthor;
using Hardened.SourceGenerator.Models;
using Hardened.SourceGenerator.Shared;

namespace Hardened.SourceGenerator.Web
{
    public static class HandlerInfoCodeGenerator
    {
        public static void Implement(WebEndPointModel endPointModel, ClassDefinition classDefinition)
        {
            var handlerInfoField = classDefinition.AddField(KnownTypes.Requests.ExecutionRequestHandlerInfo, "_handlerInfo");

            handlerInfoField.InitializeValue = "new ()";

            var handlerProperty = classDefinition.AddProperty(KnownTypes.Requests.IExecutionRequestHandlerInfo, "HandlerInfo");

            handlerProperty.Modifiers |= ComponentModifier.Public | ComponentModifier.Override;
            handlerProperty.Get.LambdaSyntax = true;
            handlerProperty.Set = null;

            handlerProperty.Get.AddCode("_handlerInfo;");
        }
    }
}
