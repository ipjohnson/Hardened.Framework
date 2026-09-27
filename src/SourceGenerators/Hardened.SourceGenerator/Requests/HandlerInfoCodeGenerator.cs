using System.Collections.Generic;
using System.Linq;
using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;
using static CSharpAuthor.SyntaxHelpers;

namespace Hardened.SourceGenerator.Requests;

public static class HandlerInfoCodeGenerator
{
    public static void Implement(RequestHandlerModel handlerModel, ClassDefinition classDefinition)
    {
        CreateParameterInfoField(handlerModel, classDefinition);

        CreateHandlerInfoField(handlerModel, classDefinition);
    }

    private static void CreateParameterInfoField(
        RequestHandlerModel requestHandlerModel,
        ClassDefinition classDefinition
    )
    {
        if (requestHandlerModel.RequestParameterInformationList.Count > 0)
        {
            var parameterInfoField = classDefinition.AddField(
                KnownTypes.Requests.IExecutionRequestParameter.MakeArray(),
                "_parameterInfo"
            );

            parameterInfoField.InitializeValue = new CodeOutputComponent("CreateParameterInfo()");

            parameterInfoField.Modifiers =
                ComponentModifier.Private | ComponentModifier.Static | ComponentModifier.Readonly;

            var method = classDefinition.AddMethod("CreateParameterInfo");

            method.Modifiers = ComponentModifier.Private | ComponentModifier.Static;
            method.SetReturnType(KnownTypes.Requests.IExecutionRequestParameter.MakeArray());

            var newArray = NewArray(
                KnownTypes.Requests.IExecutionRequestParameter,
                requestHandlerModel.RequestParameterInformationList.Count
            );

            var array = method.Assign(newArray).ToVar("returnArray");

            for (var i = 0; i < requestHandlerModel.RequestParameterInformationList.Count; i++)
            {
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

    private static void CreateHandlerInfoField(
        RequestHandlerModel handlerModel,
        ClassDefinition classDefinition
    )
    {
        // Emit _metadata BEFORE _handlerInfo so static initialization order is correct
        var metadataArg = "";
        if (handlerModel.Filters.Count > 0)
        {
            CreateMetadataField(handlerModel, classDefinition);
            metadataArg = ", _metadata";
        }

        var handlerInfoField = classDefinition.AddField(
            KnownTypes.Requests.ExecutionRequestHandlerInfo,
            "_handlerInfo"
        );

        handlerInfoField.Modifiers =
            ComponentModifier.Private | ComponentModifier.Static | ComponentModifier.Readonly;

        var parameterInfoField = "";

        if (handlerModel.RequestParameterInformationList.Count > 0)
        {
            parameterInfoField = ", _parameterInfo";
        }
        else if (metadataArg.Length > 0)
        {
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

        if (handlerModel.ResponseInformation.DefaultStatusCode is { } successStatus)
        {
            declaredArgs += $", successStatus: {successStatus}";
        }

        if (!string.IsNullOrEmpty(handlerModel.ResponseInformation.NullResponseBodyExpression))
        {
            declaredArgs +=
                $", nullResponseBody: {handlerModel.ResponseInformation.NullResponseBodyExpression}";
        }

        // The bodies the contract declares per status, for the refusals the pipeline raises itself.
        // A framework exception carries no body, so without this a document promising a Problem for
        // its 401 answered a shape the document never described.
        if (DeclaredErrorBodies(handlerModel) is { } declaredErrorBodies)
        {
            declaredArgs += ", declaredErrorBodies: " + declaredErrorBodies;
        }

        // The headers the operation reads, which a CORS preflight allows beside the configured ones.
        if (RequestHeaders(handlerModel) is { Count: > 0 } requestHeaders)
        {
            declaredArgs +=
                ", requestHeaders: new string[] { "
                + string.Join(", ", requestHeaders.Select(Quote))
                + " }";
        }

        // The query keys the operation binds, which VaryByQuery with no keys varies a cached
        // response on.
        if (QueryParameters(handlerModel) is { Count: > 0 } queryParameters)
        {
            declaredArgs +=
                ", queryParameters: new string[] { "
                + string.Join(", ", queryParameters.Select(Quote))
                + " }";
        }

        // How a thrown framework record becomes the body declared at its status. A contract's only:
        // a code-first handler's declared bodies are the records themselves.
        if (
            !string.IsNullOrEmpty(
                handlerModel.ResponseInformation.DeclaredErrorConversionsExpression
            )
        )
        {
            declaredArgs +=
                ", declaredErrorConversions: "
                + handlerModel.ResponseInformation.DeclaredErrorConversionsExpression;
        }

        // The media types this operation produces, as the array negotiation reads. Emitted only when
        // the operation declared some - an empty array and no array mean different things, and the
        // second is what leaves an unannotated handler negotiating exactly as it did.
        if (!string.IsNullOrEmpty(handlerModel.ResponseInformation.ProducedContentTypes))
        {
            var quoted = handlerModel
                .ResponseInformation.ProducedContentTypes!.Split(',')
                .Select(contentType => "\"" + contentType.Trim() + "\"");

            declaredArgs +=
                $", producedContentTypes: new string[] {{ {string.Join(", ", quoted)} }}";
        }

        // And the ones a description declared its failures with, where they are not the same set.
        // A failure negotiates within these, so a contract's problem+json 404 is not answered in
        // the media type its 200 declares.
        if (
            ErrorContentTypes(handlerModel.ResponseInformation) is { } errorContentTypes
            && errorContentTypes.Length > 0
        )
        {
            var quoted = errorContentTypes.Select(contentType => "\"" + contentType + "\"");

            declaredArgs += $", errorContentTypes: new string[] {{ {string.Join(", ", quoted)} }}";
        }

        // The body parameter's identifier, so a deserialization failure names its fields with the
        // prefix the generated validators use rather than a hardcoded "body".
        foreach (var parameter in handlerModel.RequestParameterInformationList)
        {
            if (parameter.BindingType == ParameterBindType.Body)
            {
                declaredArgs += $", bodyParameterName: \"{parameter.Name}\"";

                break;
            }
        }

        // The status the contract declares validation failures answer with. The converter keeps
        // the promise at run time; without this the build published a 422 the service never sent.
        if (handlerModel.ResponseInformation.ValidationErrorStatus is { } validationStatus)
        {
            declaredArgs += $", validationErrorStatus: {validationStatus}";
        }

        // Whether the handler streams, which only the return type knows. The conditional-GET stage
        // reads it to stand down rather than buffer an event stream; nothing at run time could
        // otherwise tell a streamed handler from a buffered one before the first item is written.
        if (handlerModel.ResponseInformation.IsAsyncEnumerable)
        {
            declaredArgs += ", streamsResponse: true";
        }

        // Whether the handler writes its own bytes, which only the return type knows. It decides
        // whether a serializer is bound to this handler's pipeline at all - a byte[] or a Stream
        // never reaches one.
        if (handlerModel.ResponseInformation.WritesRawBytes)
        {
            declaredArgs += ", writesRawBytes: true";
        }

        // The type is handed over rather than named, so it is still a type when the file is
        // serialized: written qualified in a file that qualifies, and counted in the using list.
        // Spelled into the string it was neither, and resolved only while some other part of the
        // file happened to import the namespace.
        handlerInfoField.InitializeValue = CodeOutputComponent.FromParts(
            new object[]
            {
                "new ",
                KnownTypes.Requests.ExecutionRequestHandlerInfo,
                $"(\"{handlerModel.Name.Path}\", \"{handlerModel.Name.Method}\", typeof(",
                handlerModel.ControllerType,
                $"), \"{handlerModel.HandlerMethod}\"{parameterInfoField}{metadataArg}{declaredArgs})",
            }
        );

        // No HandlerInfo property is emitted, deliberately. The field above is the handler as
        // written; BaseExecutionHandler exposes the one the chain was actually built from, which is
        // that plus whatever conventions contributed. A property here would return the wrong one and
        // shadow the right one - see BaseExecutionHandler.HandlerInfo.
    }

    private static void CreateMetadataField(
        RequestHandlerModel handlerModel,
        ClassDefinition classDefinition
    )
    {
        var arguments = new List<object>();

        foreach (var filterInformation in handlerModel.Filters)
        {
            var newValue = New(
                (ITypeDefinition)filterInformation.TypeDefinition,
                new CodeOutputComponent(filterInformation.Arguments) { Indented = false }
            );

            if (!string.IsNullOrEmpty(filterInformation.PropertyAssignment))
            {
                newValue.AddInitValue(filterInformation.PropertyAssignment);
            }

            arguments.Add(newValue);
        }

        var metadataField = classDefinition.AddField(typeof(object).MakeArrayType(), "_metadata");
        metadataField.Modifiers =
            ComponentModifier.Private | ComponentModifier.Static | ComponentModifier.Readonly;
        metadataField.InitializeValue = NewArray(typeof(object), arguments.ToArray());
    }

    /// <summary>
    /// The bodies the operation declares for its failures, as the dictionary the handler info takes,
    /// or null where it declares none that can be shared.
    /// </summary>
    /// <remarks>
    /// A contract's are built by the bridge, from the schemas it can fill. A code-first handler's
    /// come from the response set and <c>[Throws&lt;T&gt;]</c>: a case whose type carries a shared
    /// <c>Default</c>, which is every problem record Hardened ships but <c>RateLimited</c>. The first
    /// such body at a status is the one a refusal at that status writes.
    /// </remarks>
    internal static string? DeclaredErrorBodies(RequestHandlerModel handlerModel)
    {
        if (!string.IsNullOrEmpty(handlerModel.ResponseInformation.DeclaredErrorBodiesExpression))
        {
            return handlerModel.ResponseInformation.DeclaredErrorBodiesExpression;
        }

        var entries = new SortedDictionary<int, string>();

        foreach (var response in handlerModel.ResponseSchemas)
        {
            if (response.DeclaredInstance != null && !entries.ContainsKey(response.Status))
            {
                entries[response.Status] = response.DeclaredInstance;
            }
        }

        if (entries.Count == 0)
        {
            return null;
        }

        return "new global::System.Collections.Generic.Dictionary<int, object> { "
            + string.Join(", ", entries.Select(entry => $"{{ {entry.Key}, {entry.Value} }}"))
            + " }";
    }

    /// <summary>
    /// The media types a description declared its failures with, or null where there is nothing to
    /// say apart from the produced set.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Null where they are the whole produced set, which is every contract declaring one media type
    /// for everything: negotiating a failure within either list gives the same answer, and emitting
    /// it would change the generated code of every such operation for nothing.
    /// </para>
    /// <para>
    /// Null as well where the operation negotiates nothing. A dispatch protocol such as Smithy's
    /// <c>awsJson1_0</c> names one media type for the document and leaves the produced set empty,
    /// so its failures are written by the default serializer, and a set here would send them to a
    /// media type nothing writes.
    /// </para>
    /// </remarks>
    internal static string[]? ErrorContentTypes(ResponseInformationModel response)
    {
        if (
            string.IsNullOrEmpty(response.ErrorContentTypes)
            || string.IsNullOrEmpty(response.ProducedContentTypes)
        )
        {
            return null;
        }

        var failures = Split(response.ErrorContentTypes!);
        var produced = Split(response.ProducedContentTypes!);

        return failures.SequenceEqual(produced, StringComparer.OrdinalIgnoreCase) ? null : failures;
    }

    private static string[] Split(string joined) =>
        joined
            .Split(',')
            .Select(contentType => contentType.Trim())
            .Where(contentType => contentType.Length > 0)
            .ToArray();

    /// <summary>
    /// The wire names of the headers the operation binds and the ones its filters declare reading,
    /// each once, in declaration order.
    /// </summary>
    private static List<string> RequestHeaders(RequestHandlerModel handlerModel)
    {
        var headers = new List<string>();

        foreach (var parameter in handlerModel.RequestParameterInformationList)
        {
            if (parameter.BindingType == ParameterBindType.Header)
            {
                Add(WireName(parameter));
            }
        }

        foreach (var declared in handlerModel.DeclaredHeaderParameters)
        {
            Add(declared.Name);
        }

        return headers;

        void Add(string name)
        {
            if (!headers.Contains(name, System.StringComparer.OrdinalIgnoreCase))
            {
                headers.Add(name);
            }
        }
    }

    /// <summary>
    /// The wire names of the query keys the operation binds, each once, in declaration order. A
    /// model bound from the query string contributes its members, as the document lists them.
    /// </summary>
    public static List<string> QueryParameters(RequestHandlerModel handlerModel)
    {
        var keys = new List<string>();

        foreach (var parameter in handlerModel.RequestParameterInformationList)
        {
            if (parameter.BindingType != ParameterBindType.QueryString)
            {
                continue;
            }

            if (parameter.Model is { Problem: null } model)
            {
                foreach (var member in model.Members)
                {
                    Add(WireName(member.Value));
                }
            }
            else
            {
                Add(WireName(parameter));
            }
        }

        return keys;

        void Add(string name)
        {
            if (!keys.Contains(name, System.StringComparer.Ordinal))
            {
                keys.Add(name);
            }
        }
    }

    private static string WireName(RequestParameterInformation parameter) =>
        string.IsNullOrEmpty(parameter.BindingName) ? parameter.Name : parameter.BindingName;

    private static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
