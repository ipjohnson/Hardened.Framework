using System;
using System.Collections.Generic;
using System.Text;
using CSharpAuthor;
using static CSharpAuthor.SyntaxHelpers;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;

namespace Hardened.SourceGenerator.Requests
{
    public static class ParametersClassGenerator
    {
        public static ClassDefinition GenerateParametersClass(RequestHandlerModel handlerModel, IConstructContainer constructContainer, string parameterClassName = "Parameters")
        {
            var parametersClass = constructContainer.AddClass(parameterClassName);

            parametersClass.Modifiers = ComponentModifier.Public | ComponentModifier.Partial;
            parametersClass.AddBaseType(KnownTypes.Requests.IExecutionRequestParameters);

            WriteProperties(handlerModel, parametersClass);

            WriteTryGetParameter(handlerModel, parametersClass);

            WriteTrySetParameter(handlerModel, parametersClass);

            WriteItemProperty(handlerModel, parametersClass);

            WriteParameterCount(handlerModel, parametersClass);

            WritePropertyInfo(handlerModel, parametersClass);

            WriteCloneMethod(handlerModel, parametersClass);

            return parametersClass;
        }

        private static void WriteCloneMethod(RequestHandlerModel handlerModel, ClassDefinition parametersClass)
        {
            var cloneMethod = parametersClass.AddMethod("Clone");
            cloneMethod.SetReturnType(KnownTypes.Requests.IExecutionRequestParameters);

            var newStatement = New(InvokeClassGenerator.GenericParameters);

            foreach (var parameterInformation in handlerModel.RequestParameterInformationList)
            {
                newStatement.AddInitValue($"{parameterInformation.Name} = {parameterInformation.Name}");
            }

            cloneMethod.Return(newStatement);
        }

        private static void WritePropertyInfo(RequestHandlerModel handlerModel, ClassDefinition parametersClass)
        {
            var parametersProperty = 
                parametersClass.AddProperty(KnownTypes.Requests.IReadOnlyListExecutionRequestParameter, "Info");

            parametersProperty.Set = null;
            parametersProperty.Get.LambdaSyntax = true;
            parametersProperty.Get.AddCode("_parameterInfo;");
        }

        private static void WriteParameterCount(RequestHandlerModel handlerModel, ClassDefinition parametersClass)
        {
            var parameterCount = parametersClass.AddProperty(typeof(int), "ParameterCount");

            parameterCount.Set = null;

            parameterCount.Get.LambdaSyntax = true;
            parameterCount.Get.AddCode(handlerModel.RequestParameterInformationList.Count + ";");
        }

        private static void WriteItemProperty(RequestHandlerModel handlerModel, ClassDefinition parametersClass)
        {
            var indexProperty = parametersClass.AddProperty(typeof(object), "this");

            indexProperty.IndexName = "index";
            indexProperty.IndexType = TypeDefinition.Get(typeof(int));

            WriteItemGetProperty(handlerModel, indexProperty);
            WriteItemSetProperty(handlerModel, indexProperty);
        }

        private static void WriteItemGetProperty(RequestHandlerModel handlerModel, PropertyDefinition indexProperty)
        {
            var switchStatement = indexProperty.Get.Switch("index");
            var index = 0;

            foreach (var parameterInformation in handlerModel.RequestParameterInformationList)
            {
                var caseBlock = switchStatement.AddCase(index++);

                caseBlock.Return(parameterInformation.Name);
            }

            var throwMessage =
                $"\"Index out of range, parameters count {handlerModel.RequestParameterInformationList.Count}, index was \" + index";

            indexProperty.Get.Throw(typeof(IndexOutOfRangeException), throwMessage);
        }

        private static void WriteItemSetProperty(RequestHandlerModel handlerModel, PropertyDefinition indexProperty)
        {
            var switchStatement = indexProperty.Set!.Switch("index");
            var index = 0;

            foreach (var parameterInformation in handlerModel.RequestParameterInformationList)
            {
                var caseBlock = switchStatement.AddCase(index++);

                caseBlock.Assign(StaticCast(parameterInformation.ParameterType, "value")).To(parameterInformation.Name);
                caseBlock.Break();
            }

            var throwMessage =
                $"\"Index out of range, parameters count {handlerModel.RequestParameterInformationList.Count}, index was \" + index";

            indexProperty.Set!.Throw(typeof(IndexOutOfRangeException), throwMessage);
        }

        private static void WriteTrySetParameter(RequestHandlerModel handlerModel, ClassDefinition parametersClass)
        {
            var setMethodDefinition = parametersClass.AddMethod("TrySetParameter");

            var parameterName = setMethodDefinition.AddParameter(typeof(string), "parameterName");
            var valueVar = setMethodDefinition.AddParameter(typeof(object), "value");

            setMethodDefinition.SetReturnType(typeof(bool));

            var switchBlock = setMethodDefinition.Switch(parameterName);

            foreach (var parameterInformation in handlerModel.RequestParameterInformationList)
            {
                var caseBlock = switchBlock.AddCase(QuoteString(parameterInformation.Name));
                caseBlock.Assign(StaticCast(parameterInformation.ParameterType, valueVar)).To(parameterInformation.Name);
                caseBlock.Return("true");
            }
            
            setMethodDefinition.Return("false");
        }

        private static void WriteTryGetParameter(RequestHandlerModel handlerModel, ClassDefinition parametersClass)
        {
            var getMethodDefinition = parametersClass.AddMethod("TryGetParameter");
            var outType = TypeDefinition.Get(typeof(object)).MakeNullable();

            var parameterName = getMethodDefinition.AddParameter(typeof(string), "parameterName");
            var valueVar = getMethodDefinition.AddParameter(outType, "value");
            valueVar.IsOut = true;

            getMethodDefinition.SetReturnType(typeof(bool));

            var switchBlock = getMethodDefinition.Switch(parameterName);

            foreach (var parameterInformation in handlerModel.RequestParameterInformationList)
            {
                var caseBlock = switchBlock.AddCase(QuoteString(parameterInformation.Name));
                caseBlock.Assign(parameterInformation.Name).To(valueVar);
                caseBlock.Return("true");
            }

            getMethodDefinition.Assign(Null()).To(valueVar);
            getMethodDefinition.Return("false");
        }

        private static void WriteProperties(RequestHandlerModel handlerModel, ClassDefinition parametersClass)
        {
            foreach (var requestParameterInformation in handlerModel.RequestParameterInformationList)
            {
                parametersClass.AddProperty(requestParameterInformation.ParameterType,
                    requestParameterInformation.Name);
            }
        }
    }
}
