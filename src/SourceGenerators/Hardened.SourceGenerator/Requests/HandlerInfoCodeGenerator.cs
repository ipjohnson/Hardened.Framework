using CSharpAuthor;
using static CSharpAuthor.SyntaxHelpers;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;

namespace Hardened.SourceGenerator.Requests;

public static class HandlerInfoCodeGenerator {
    public static void Implement(RequestHandlerModel handlerModel, ClassDefinition classDefinition) {
        CreateParameterInfoField(handlerModel, classDefinition);

        CreateHandlerInfoField(handlerModel, classDefinition);
    }

    private static void CreateParameterInfoField(RequestHandlerModel requestHandlerModel,
        ClassDefinition classDefinition) {
        if (requestHandlerModel.RequestParameterInformationList.Count > 0) {
            var parameterInfoField = classDefinition.AddField(
                KnownTypes.Requests.IExecutionRequestParameter.MakeArray(),
                "_parameterInfo");

            parameterInfoField.InitializeValue = new CodeOutputComponent("CreateParameterInfo()");

            parameterInfoField.Modifiers =
                ComponentModifier.Private | ComponentModifier.Static | ComponentModifier.Readonly;

            var method = classDefinition.AddMethod("CreateParameterInfo");

            method.Modifiers = ComponentModifier.Private | ComponentModifier.Static;
            method.SetReturnType(KnownTypes.Requests.IExecutionRequestParameter.MakeArray());

            var newArray = NewArray(KnownTypes.Requests.IExecutionRequestParameter,
                requestHandlerModel.RequestParameterInformationList.Count);

            var array = method.Assign(newArray).ToVar("returnArray");

            for (var i = 0; i < requestHandlerModel.RequestParameterInformationList.Count; i++) {
                var parameterInfo = requestHandlerModel.RequestParameterInformationList[i];

                var parameter = New(
                    KnownTypes.Requests.ExecutionRequestParameter,
                    QuoteString(parameterInfo.Name),
                    i,
                    TypeOf(parameterInfo.ParameterType.MakeNullable(false))
                );

                method.Assign(parameter).To($"returnArray[{i}]");
            }

            method.Return(array);
        }
    }

    private static void CreateHandlerInfoField(RequestHandlerModel handlerModel, ClassDefinition classDefinition) {
        // Emit _metadata BEFORE _handlerInfo so static initialization order is correct
        var metadataArg = "";
        if (handlerModel.Filters.Count > 0) {
            CreateMetadataField(handlerModel, classDefinition);
            metadataArg = ", _metadata";
        }

        var handlerInfoField =
            classDefinition.AddField(KnownTypes.Requests.ExecutionRequestHandlerInfo, "_handlerInfo");

        handlerInfoField.Modifiers =
            ComponentModifier.Private | ComponentModifier.Static | ComponentModifier.Readonly;

        var parameterInfoField = "";

        if (handlerModel.RequestParameterInformationList.Count > 0) {
            parameterInfoField = ", _parameterInfo";
        }

        handlerInfoField.InitializeValue =
            new CodeOutputComponent($"new ExecutionRequestHandlerInfo(\"{handlerModel.Name.Path}\", \"{handlerModel.Name.Method}\", typeof({handlerModel.ControllerType.Name}), \"{handlerModel.HandlerMethod}\"{parameterInfoField}{metadataArg})");

        var handlerProperty =
            classDefinition.AddProperty(KnownTypes.Requests.IExecutionRequestHandlerInfo, "HandlerInfo");

        handlerProperty.Modifiers |= ComponentModifier.Public | ComponentModifier.Override;
        handlerProperty.Get.LambdaSyntax = true;
        handlerProperty.Set = null;

        handlerProperty.Get.AddCode("_handlerInfo;");
    }

    private static void CreateMetadataField(RequestHandlerModel handlerModel, ClassDefinition classDefinition) {
        var arguments = new List<object>();

        foreach (var filterInformation in handlerModel.Filters) {
            var newValue = New((ITypeDefinition)filterInformation.TypeDefinition, new CodeOutputComponent(filterInformation.Arguments) {
                Indented = false
            });

            if (!string.IsNullOrEmpty(filterInformation.PropertyAssignment)) {
                newValue.AddInitValue(filterInformation.PropertyAssignment);
            }

            arguments.Add(newValue);
        }

        var metadataField = classDefinition.AddField(typeof(object).MakeArrayType(), "_metadata");
        metadataField.Modifiers = ComponentModifier.Private | ComponentModifier.Static | ComponentModifier.Readonly;
        metadataField.InitializeValue = NewArray(typeof(object), arguments.ToArray());
    }
}