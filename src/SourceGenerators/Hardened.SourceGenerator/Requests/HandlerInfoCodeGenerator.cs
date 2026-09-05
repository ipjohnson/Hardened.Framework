using System.Linq;
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
        else if (metadataArg.Length > 0) {
            // Both are optional constructor arguments, and parameters comes first. A handler
            // with metadata but no parameters must still fill the parameters slot, or the
            // metadata array lands in it and the generated code does not compile.
            parameterInfoField = ", null";
        }

        // The status the operation declared, and the body a null return writes.
        //
        // Both sit after `requirement`, which nothing generated passes, so both are written by name.
        // Emitted only when there is something to say - an operation answering 200 with no declared
        // null body produces exactly the constructor call it always did.
        var declaredArgs = "";

        if (handlerModel.ResponseInformation.DefaultStatusCode is { } successStatus) {
            declaredArgs += $", successStatus: {successStatus}";
        }

        if (!string.IsNullOrEmpty(handlerModel.ResponseInformation.NullResponseBodyExpression)) {
            declaredArgs +=
                $", nullResponseBody: {handlerModel.ResponseInformation.NullResponseBodyExpression}";
        }

        // The media types this operation produces, as the array negotiation reads. Emitted only when
        // the operation declared some - an empty array and no array mean different things, and the
        // second is what leaves an unannotated handler negotiating exactly as it did.
        if (!string.IsNullOrEmpty(handlerModel.ResponseInformation.ProducedContentTypes)) {
            var quoted = handlerModel.ResponseInformation.ProducedContentTypes!
                .Split(',')
                .Select(contentType => "\"" + contentType.Trim() + "\"");

            declaredArgs += $", producedContentTypes: new string[] {{ {string.Join(", ", quoted)} }}";
        }

        // The body parameter's identifier, so a deserialization failure names its fields with the
        // prefix the generated validators use rather than a hardcoded "body".
        foreach (var parameter in handlerModel.RequestParameterInformationList) {
            if (parameter.BindingType == ParameterBindType.Body) {
                declaredArgs += $", bodyParameterName: \"{parameter.Name}\"";

                break;
            }
        }

        // The status the contract declares validation failures answer with. The converter keeps
        // the promise at run time; without this the build published a 422 the service never sent.
        if (handlerModel.ResponseInformation.ValidationErrorStatus is { } validationStatus) {
            declaredArgs += $", validationErrorStatus: {validationStatus}";
        }

        // Whether the handler streams, which only the return type knows. The conditional-GET stage
        // reads it to stand down rather than buffer an event stream; nothing at run time could
        // otherwise tell a streamed handler from a buffered one before the first item is written.
        if (handlerModel.ResponseInformation.IsAsyncEnumerable) {
            declaredArgs += ", streamsResponse: true";
        }

        // The type is handed over rather than named, so it is still a type when the file is
        // serialized: written qualified in a file that qualifies, and counted in the using list.
        // Spelled into the string it was neither, and resolved only while some other part of the
        // file happened to import the namespace.
        handlerInfoField.InitializeValue =
            CodeOutputComponent.FromParts(new object[] {
                "new ",
                KnownTypes.Requests.ExecutionRequestHandlerInfo,
                $"(\"{handlerModel.Name.Path}\", \"{handlerModel.Name.Method}\", typeof(",
                handlerModel.ControllerType,
                $"), \"{handlerModel.HandlerMethod}\"{parameterInfoField}{metadataArg}{declaredArgs})"
            });

        // No HandlerInfo property is emitted, deliberately. The field above is the handler as
        // written; BaseExecutionHandler exposes the one the chain was actually built from, which is
        // that plus whatever conventions contributed. A property here would return the wrong one and
        // shadow the right one - see BaseExecutionHandler.HandlerInfo.
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