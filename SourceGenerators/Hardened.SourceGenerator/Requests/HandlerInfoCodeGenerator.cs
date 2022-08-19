using System;
using System.Collections.Generic;
using System.Text;
using CSharpAuthor;
using Hardened.SourceGenerator.Models;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;

namespace Hardened.SourceGenerator.Requests
{
    public static class HandlerInfoCodeGenerator
    {

        public static void Implement(RequestHandlerModel handlerModel, ClassDefinition classDefinition)
        {
            var handlerInfoField = classDefinition.AddField(KnownTypes.Requests.ExecutionRequestHandlerInfo, "_handlerInfo");

            handlerInfoField.Modifiers =
                ComponentModifier.Private | ComponentModifier.Static | ComponentModifier.Readonly;

            handlerInfoField.InitializeValue =
                $"new ExecutionRequestHandlerInfo(\"{handlerModel.Name.Path}\", \"{handlerModel.Name.Method}\", typeof({handlerModel.ControllerType.Name}), \"{handlerModel.HandlerMethod}\")";

            var handlerProperty = classDefinition.AddProperty(KnownTypes.Requests.IExecutionRequestHandlerInfo, "HandlerInfo");

            handlerProperty.Modifiers |= ComponentModifier.Public | ComponentModifier.Override;
            handlerProperty.Get.LambdaSyntax = true;
            handlerProperty.Set = null;

            handlerProperty.Get.AddCode("_handlerInfo;");
        }
    }
}
