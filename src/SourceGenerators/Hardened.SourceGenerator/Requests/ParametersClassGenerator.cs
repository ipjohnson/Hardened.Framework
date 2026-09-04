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

        // The validated shape, when something produced one. Implementing it is what lets a filter
        // hold IValidatorFor<TInterface> and hand this instance to it - no reflection, one type
        // test. The properties below satisfy it by name; nothing extra is emitted for it.
        if (handlerModel.ParametersInterface != null) {
            parametersClass.AddBaseType(handlerModel.ParametersInterface);
        }

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

    /// <summary>
    /// Every member reached through <c>this.</c>, because the indexer's own parameter is in scope.
    /// </summary>
    /// <remarks>
    /// The indexer is <c>this[int index]</c>, so a request parameter named <c>index</c> is shadowed
    /// by it: unqualified, <c>return index</c> hands back the <em>ordinal</em> rather than the
    /// parameter's value, and it compiles, because both are ints. Gitea's specification declares a
    /// path parameter called <c>index</c> and only failed loudly because it is an <c>int64</c> - a
    /// 32-bit one would have been silently wrong at runtime.
    ///
    /// <para>
    /// Qualifying rather than renaming the indexer's parameter: the name a specification chooses is
    /// not ours to predict, and <c>this.</c> holds whatever it is.
    /// </para>
    /// </remarks>
    private static void WriteItemGetProperty(RequestHandlerModel handlerModel, PropertyDefinition indexProperty) {
        var switchStatement = indexProperty.Get.Switch("index");
        var index = 0;

        foreach (var parameterInformation in handlerModel.RequestParameterInformationList) {
            var caseBlock = switchStatement.AddCase(index++);

            caseBlock.Return("this." + parameterInformation.MemberName + "!");
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

            // this., for the reason WriteItemGetProperty gives.
            caseBlock.Assign(StaticCast(parameterInformation.ParameterType, "value"))
                .To("this." + parameterInformation.MemberName);

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
                requestParameterInformation.MemberName);

            property.DefaultValue = CodeOutputComponent.Get("default!");
        }
    }
}