using System.Diagnostics.CodeAnalysis;
using CSharpAuthor;
using static CSharpAuthor.SyntaxHelpers;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;

namespace Hardened.SourceGenerator.Requests;

public static class ParametersClassGenerator {
    public static ClassDefinition GenerateParametersClass(RequestHandlerModel handlerModel,
        IConstructContainer constructContainer, string parameterClassName = "Parameters") {
        var parametersClass = constructContainer.AddClass(parameterClassName);

        parametersClass.Modifiers = ComponentModifier.Public | ComponentModifier.Partial;

        // Derives from the runtime base rather than implementing the interface directly. Lookup
        // by name, ParameterCount and Clone all follow from this[int] and Info, so they are
        // implemented once there instead of emitted into every handler - about half the IL of
        // this class. Only what the compiler knows and the base cannot is generated below.
        parametersClass.AddBaseType(KnownTypes.Requests.ExecutionRequestParameters);

        WriteProperties(handlerModel, parametersClass);

        WriteItemProperty(handlerModel, parametersClass);

        WritePropertyInfo(handlerModel, parametersClass);

        return parametersClass;
    }

    private static void WritePropertyInfo(RequestHandlerModel handlerModel, ClassDefinition parametersClass) {
        var parametersProperty =
            parametersClass.AddProperty(KnownTypes.Requests.IReadOnlyListExecutionRequestParameter, "Info");

        parametersProperty.Modifiers |= ComponentModifier.Override;
        parametersProperty.Set = null;
        parametersProperty.Get.LambdaSyntax = true;
        parametersProperty.Get.AddCode("_parameterInfo;");
    }

    private static void WriteItemProperty(RequestHandlerModel handlerModel, ClassDefinition parametersClass) {
        var indexProperty = parametersClass.AddProperty(typeof(object), "this");

        indexProperty.Modifiers |= ComponentModifier.Override;
        indexProperty.IndexName = "index";
        indexProperty.IndexType = TypeDefinition.Get(typeof(int));

        WriteItemGetProperty(handlerModel, indexProperty);
        WriteItemSetProperty(handlerModel, indexProperty);
    }

    private static void WriteItemGetProperty(RequestHandlerModel handlerModel, PropertyDefinition indexProperty) {
        var switchStatement = indexProperty.Get.Switch("index");
        var index = 0;

        foreach (var parameterInformation in handlerModel.RequestParameterInformationList) {
            var caseBlock = switchStatement.AddCase(index++);

            caseBlock.Return(parameterInformation.Name + "!");
        }

        var throwMessage =
            $"\"Index out of range, parameters count {handlerModel.RequestParameterInformationList.Count}, index was \" + index";

        indexProperty.Get.Throw(typeof(IndexOutOfRangeException), throwMessage);
    }

    private static void WriteItemSetProperty(RequestHandlerModel handlerModel, PropertyDefinition indexProperty) {
        var switchStatement = indexProperty.Set!.Switch("index");
        var index = 0;

        foreach (var parameterInformation in handlerModel.RequestParameterInformationList) {
            var caseBlock = switchStatement.AddCase(index++);

            caseBlock.Assign(StaticCast(parameterInformation.ParameterType, "value")).To(parameterInformation.Name);

            // return, not break: break leaves the switch and falls into the throw below, so every
            // set threw IndexOutOfRangeException including the valid ones. The getter always used
            // return and was unaffected, which is why nothing caught this. Found 2026-08-12.
            caseBlock.Return();
        }

        var throwMessage =
            $"\"Index out of range, parameters count {handlerModel.RequestParameterInformationList.Count}, index was \" + index";

        indexProperty.Set!.Throw(typeof(IndexOutOfRangeException), throwMessage);
    }

    private static void WriteProperties(RequestHandlerModel handlerModel, ClassDefinition parametersClass) {
        foreach (var requestParameterInformation in handlerModel.RequestParameterInformationList) {
            var property = parametersClass.AddProperty(
                requestParameterInformation.ParameterType,
                requestParameterInformation.Name);

            property.DefaultValue = CodeOutputComponent.Get("default!");
        }
    }
}