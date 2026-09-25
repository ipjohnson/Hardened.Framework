using CSharpAuthor;
using CSharpAuthor.Expressions;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static CSharpAuthor.SyntaxHelpers;

namespace Hardened.SourceGenerator.Requests;

public static class BindRequestParametersMethodGenerator
{
    private static readonly ITypeDefinition StringValuesType = TypeDefinition.Get(
        "Microsoft.Extensions.Primitives",
        "StringValues"
    );

    private static readonly ITypeDefinition FormFileBindingType = TypeDefinition.Get(
        "Hardened.Requests.Runtime.Forms",
        "FormFileBinding"
    );

    private static readonly ITypeDefinition FormBindingType = TypeDefinition.Get(
        "Hardened.Requests.Runtime.Forms",
        "FormBinding"
    );

    public static void Implement(
        RequestHandlerModel requestHandlerModel,
        ClassDefinition classDefinition
    )
    {
        var invokeMethod = classDefinition.AddMethod("BindRequestParameters");

        invokeMethod.Modifiers = ComponentModifier.Private | ComponentModifier.Static;

        var needsAsync = requestHandlerModel.RequestParameterInformationList.Any(p =>
            (p.BindingType == ParameterBindType.Body && !BindsSynchronously(p))
            || p.BindingType == ParameterBindType.CustomAttribute
            || p.BindingType == ParameterBindType.Form
        );

        if (needsAsync)
        {
            invokeMethod.Modifiers |= ComponentModifier.Async;
        }

        invokeMethod.SetReturnType(
            new GenericTypeDefinition(
                typeof(Task<>),
                new[] { KnownTypes.Requests.IExecutionRequestParameters }
            )
        );

        var context = invokeMethod.AddParameter(KnownTypes.Requests.IExecutionContext, "context");

        ProcessParameters(requestHandlerModel, classDefinition, invokeMethod, context, needsAsync);
    }

    /// <summary>
    /// Whether a body parameter is bound with nothing to await.
    /// </summary>
    /// <remarks>
    /// A <c>Stream</c> body is handed over rather than read, so <c>RawBody.Body</c> returns it
    /// directly and the binding has no await in it - the one body shape that does not.
    /// <c>byte[]</c> reads the stream to its end and does await, and every other body goes through
    /// the serialization service.
    /// <para>
    /// Emitted <c>async</c> anyway, the method was CS1998 in a file the author cannot edit, so a
    /// project building warnings as errors could not take a <c>Stream</c> body at all.
    /// </para>
    /// </remarks>
    private static bool BindsSynchronously(RequestParameterInformation parameter) =>
        parameter.IsRawBody && !parameter.ParameterType.IsArray;

    private static void ProcessParameters(
        RequestHandlerModel requestHandlerModel,
        ClassDefinition classDefinition,
        MethodDefinition invokeMethod,
        ParameterDefinition context,
        bool needsAsync
    )
    {
        var parametersVar = invokeMethod
            .Assign(New(InvokeClassGenerator.ParametersType(requestHandlerModel)))
            .ToVar("parameters");

        // Once per handler rather than once per parameter, because reading it reads the body. Two
        // form parameters on one handler must not read the stream twice, and the local is what
        // makes that structural rather than something FormReader has to cache against a request it
        // is not scoped to.
        //
        // Through FormBinding rather than the reader, because a handler with form parameters
        // answers 415 for a body that is not a form, and the reader reads one as an empty form.
        InstanceDefinition? formVar = null;

        if (
            requestHandlerModel.RequestParameterInformationList.Any(p =>
                p.BindingType == ParameterBindType.Form
            )
        )
        {
            formVar = invokeMethod
                .Assign(Await(Invoke(FormBindingType, "Read", context)))
                .ToVar("form");
        }

        foreach (var parameterInformation in requestHandlerModel.RequestParameterInformationList)
        {
            switch (parameterInformation.BindingType)
            {
                case ParameterBindType.Body:
                    BindBodyParameter(parameterInformation, invokeMethod, context, parametersVar);
                    break;

                case ParameterBindType.Header:
                case ParameterBindType.QueryString:
                case ParameterBindType.Path:
                case ParameterBindType.Cookie:
                    BindRequestValueToParameter(
                        parameterInformation,
                        invokeMethod,
                        context,
                        parametersVar
                    );
                    break;

                case ParameterBindType.Form:
                    BindFormValueToParameter(
                        parameterInformation,
                        invokeMethod,
                        context,
                        parametersVar,
                        formVar!
                    );
                    break;

                case ParameterBindType.ExecutionContext:
                case ParameterBindType.ExecutionRequest:
                case ParameterBindType.ExecutionResponse:
                case ParameterBindType.CancellationToken:
                    BindExecutionSpecialType(
                        parameterInformation,
                        invokeMethod,
                        context,
                        parametersVar
                    );
                    break;

                case ParameterBindType.ServiceProvider:
                    BindServiceProviderType(
                        parameterInformation,
                        invokeMethod,
                        context,
                        parametersVar
                    );
                    break;

                case ParameterBindType.FromServiceProvider:
                    BindFromServiceProviderType(
                        parameterInformation,
                        invokeMethod,
                        context,
                        parametersVar
                    );
                    break;

                case ParameterBindType.CustomAttribute:
                    BindFromCustomAttribute(
                        classDefinition,
                        parameterInformation,
                        invokeMethod,
                        context,
                        parametersVar
                    );
                    break;

                default:
                    throw new NotImplementedException(
                        "Binding not supported yet: " + parameterInformation.BindingType
                    );
            }
        }

        if (needsAsync)
        {
            invokeMethod.Return(parametersVar);
        }
        else
        {
            invokeMethod.Return(
                InvokeGeneric(
                    TypeDefinition.Get(typeof(Task)),
                    "FromResult",
                    new[] { KnownTypes.Requests.IExecutionRequestParameters },
                    parametersVar
                )
            );
        }
    }

    private static void BindFromCustomAttribute(
        ClassDefinition classDefinition,
        RequestParameterInformation parameterInformation,
        MethodDefinition invokeMethod,
        ParameterDefinition context,
        InstanceDefinition parametersVar
    )
    {
        var attributeDataStatement = InvokeGeneric(
            KnownTypes.Requests.ExecutionHelper,
            "CustomAttributeData",
            new[] { parameterInformation.ParameterType },
            new object[]
            {
                context,
                New(
                    parameterInformation.CustomAttribute!.TypeDefinition,
                    new CodeOutputComponent(parameterInformation.CustomAttribute.Arguments)
                    {
                        Indented = false,
                    }
                ),
                new CodeOutputComponent($"_parameterInfo[{parameterInformation.ParameterIndex}]"),
            }
        );

        invokeMethod
            .Assign(Await(attributeDataStatement))
            .To(parametersVar.Property(parameterInformation.MemberName));
    }

    private static void BindServiceProviderType(
        RequestParameterInformation parameterInformation,
        MethodDefinition invokeMethod,
        ParameterDefinition context,
        InstanceDefinition parametersVar
    )
    {
        invokeMethod
            .Assign(context.Property("RequestServices"))
            .To(parametersVar.Property(parameterInformation.MemberName));
    }

    private static void BindFromServiceProviderType(
        RequestParameterInformation parameterInformation,
        MethodDefinition invokeMethod,
        ParameterDefinition context,
        InstanceDefinition parametersVar
    )
    {
        IOutputComponent invokeStatement;

        if (parameterInformation.Required)
        {
            invokeStatement = context
                .Property("RequestServices")
                .InvokeGeneric("GetRequiredService", new[] { parameterInformation.ParameterType });
        }
        else
        {
            invokeStatement = context
                .Property("RequestServices")
                .InvokeGeneric("GetService", new[] { parameterInformation.ParameterType });
        }

        invokeStatement.AddUsingNamespace(
            KnownTypes.Namespace.Microsoft.Extensions.DependencyInjection
        );

        invokeMethod
            .Assign(invokeStatement)
            .To(parametersVar.Property(parameterInformation.MemberName));
    }

    private static void BindExecutionSpecialType(
        RequestParameterInformation parameterInformation,
        MethodDefinition invokeMethod,
        ParameterDefinition context,
        InstanceDefinition parametersVar
    )
    {
        IOutputComponent invokeStatement = context;

        if (parameterInformation.BindingType == ParameterBindType.ExecutionRequest)
        {
            invokeStatement = context.Property("Request");
        }
        else if (parameterInformation.BindingType == ParameterBindType.ExecutionResponse)
        {
            invokeStatement = context.Property("Response");
        }
        else if (parameterInformation.BindingType == ParameterBindType.CancellationToken)
        {
            invokeStatement = context.Property("CancellationToken");
        }

        invokeMethod
            .Assign(invokeStatement)
            .To(parametersVar.Property(parameterInformation.MemberName));
    }

    private static void BindRequestValueToParameter(
        RequestParameterInformation parameterInformation,
        MethodDefinition invokeMethod,
        ParameterDefinition context,
        InstanceDefinition parametersVar
    )
    {
        if (
            parameterInformation.BindingType == ParameterBindType.QueryString
            && parameterInformation.Model is { Problem: null } model
        )
        {
            BindModel(
                parameterInformation,
                model,
                invokeMethod,
                context,
                parametersVar,
                field =>
                    context
                        .Property("Request")
                        .Property("QueryString")
                        .Invoke("Get", QuoteString(field)),
                null
            );

            return;
        }

        var bindingName = parameterInformation.BindingName;

        if (string.IsNullOrEmpty(bindingName))
        {
            bindingName = parameterInformation.Name;
        }

        var instance = "QueryString";

        switch (parameterInformation.BindingType)
        {
            case ParameterBindType.Path:
                instance = "PathTokens";
                break;
            case ParameterBindType.Header:
                instance = "Headers";
                break;
            case ParameterBindType.Cookie:
                instance = "Cookies";
                break;
        }

        var requestValue = context
            .Property("Request")
            .Property(instance)
            .Invoke("Get", QuoteString(bindingName));

        // PathTokens and QueryString carry their own Get; Headers is a plain IDictionary and
        // Cookies a plain IReadOnlyList, so theirs comes from an extension class. An extension
        // method is reachable only through a using of its namespace - global:: cannot name one.
        if (
            parameterInformation.BindingType is ParameterBindType.Header or ParameterBindType.Cookie
        )
        {
            requestValue.AddUsingNamespace(
                KnownTypes.Namespace.Hardened.Requests.Runtime.Execution
            );
        }

        var valueStatement = Bang(requestValue);

        var stringInvokeStatement = context
            .Property("KnownServices")
            .Property("StringConverterService");

        invokeMethod
            .Assign(
                Convert(parameterInformation, stringInvokeStatement, valueStatement, bindingName)
            )
            .To(parametersVar.Property(parameterInformation.MemberName));
    }

    /// <summary>
    /// A form field, converted the same way every other string-valued source is.
    /// </summary>
    /// <remarks>
    /// The only difference from <see cref="BindRequestValueToParameter"/> is where the value comes
    /// from: a local holding the parsed form, rather than a collection hanging off
    /// <c>context.Request</c>. The conversion, the default handling and the optional handling are
    /// deliberately identical - a form field is a string that has to become a parameter type, which
    /// is what <c>IStringConverterService</c> already answers for a query value.
    /// </remarks>
    private static void BindFormValueToParameter(
        RequestParameterInformation parameterInformation,
        MethodDefinition invokeMethod,
        ParameterDefinition context,
        InstanceDefinition parametersVar,
        InstanceDefinition formVar
    )
    {
        if (FormFileType.Binds(parameterInformation.ParameterType))
        {
            invokeMethod
                .Assign(File(parameterInformation, formVar))
                .To(parametersVar.Property(parameterInformation.MemberName));

            return;
        }

        if (parameterInformation.Model is { Problem: null } model)
        {
            BindModel(
                parameterInformation,
                model,
                invokeMethod,
                context,
                parametersVar,
                field => formVar.Invoke("Get", QuoteString(field)),
                formVar
            );

            return;
        }

        var bindingName = parameterInformation.BindingName;

        if (string.IsNullOrEmpty(bindingName))
        {
            bindingName = parameterInformation.Name;
        }

        var valueStatement = Bang(formVar.Invoke("Get", QuoteString(bindingName)));

        var stringInvokeStatement = context
            .Property("KnownServices")
            .Property("StringConverterService");

        invokeMethod
            .Assign(
                Convert(parameterInformation, stringInvokeStatement, valueStatement, bindingName)
            )
            .To(parametersVar.Property(parameterInformation.MemberName));
    }

    /// <summary>
    /// The conversion call for one parameter, whatever string-valued source it came from.
    /// </summary>
    /// <remarks>
    /// Path, query, header, cookie and form all hand over a <c>StringValues</c>, so they all convert
    /// the same way and a change here reaches spec-first and code-first alike - this emitter is the
    /// only one either of them has.
    /// </remarks>
    private static IOutputComponent Convert(
        RequestParameterInformation parameterInformation,
        InstanceDefinition stringInvokeStatement,
        IOutputComponent valueStatement,
        string bindingName
    ) =>
        Convert(
            parameterInformation.ParameterType,
            parameterInformation.Required,
            parameterInformation.DefaultValue,
            stringInvokeStatement,
            valueStatement,
            bindingName
        );

    private static IOutputComponent Convert(
        ITypeDefinition type,
        bool required,
        string? defaultValue,
        InstanceDefinition stringInvokeStatement,
        IOutputComponent valueStatement,
        string bindingName
    )
    {
        var itemType = CollectionParameter.ItemType(type);

        if (itemType != null)
        {
            return ConvertMany(
                type,
                required,
                defaultValue,
                stringInvokeStatement,
                valueStatement,
                bindingName,
                itemType
            );
        }

        if (!string.IsNullOrEmpty(defaultValue))
        {
            return stringInvokeStatement.InvokeGeneric(
                "ParseWithDefault",
                new[] { type },
                valueStatement,
                QuoteString(bindingName),
                defaultValue!
            );
        }

        if (required)
        {
            return stringInvokeStatement.InvokeGeneric(
                "ParseRequired",
                new[] { type },
                valueStatement,
                QuoteString(bindingName)
            );
        }

        return stringInvokeStatement.InvokeGeneric(
            "ParseOptional",
            new[] { type },
            valueStatement,
            QuoteString(bindingName)
        );
    }

    /// <summary>
    /// A parameter the handler declared as a collection, from every value the request carried under
    /// its name.
    /// </summary>
    /// <remarks>
    /// The converter is asked for the item type and answers a <c>List</c>, which satisfies every
    /// collection interface a handler can declare. An array is the one shape it does not, so that
    /// case adds the copy - and it is a copy either way, since the list is built one item at a time.
    /// </remarks>
    private static IOutputComponent ConvertMany(
        ITypeDefinition type,
        bool required,
        string? defaultValue,
        InstanceDefinition stringInvokeStatement,
        IOutputComponent valueStatement,
        string bindingName,
        ITypeDefinition itemType
    )
    {
        required = required && string.IsNullOrEmpty(defaultValue);

        IOutputComponent invokeStatement = stringInvokeStatement.InvokeGeneric(
            required ? "ParseRequiredMany" : "ParseOptionalMany",
            new[] { itemType },
            valueStatement,
            QuoteString(bindingName)
        );

        if (type.IsArray)
        {
            // Null-conditional on the optional side: an absent parameter stays absent rather than
            // becoming an empty array, which is the distinction ParseOptionalMany draws.
            invokeStatement = required
                ? invokeStatement.Invoke("ToArray")
                : Question(invokeStatement).Invoke("ToArray");

            invokeStatement.AddUsingNamespace("System.Linq");
        }

        if (!string.IsNullOrEmpty(defaultValue))
        {
            return NullCoalesce(invokeStatement, defaultValue!);
        }

        return invokeStatement;
    }

    /// <summary>
    /// A file, or every file, the form carried under a parameter's name.
    /// </summary>
    /// <remarks>
    /// Checked by <c>FormFileBinding</c> rather than the string converter, which has nothing to
    /// convert, and refused with the same <c>required</c> a missing field gets. A list, array or
    /// read-only collection takes what <c>GetFiles</c> answers, copied only where the declared type
    /// needs one.
    /// </remarks>
    private static IOutputComponent File(
        RequestParameterInformation parameter,
        InstanceDefinition formVar
    )
    {
        var name = string.IsNullOrEmpty(parameter.BindingName)
            ? parameter.Name
            : parameter.BindingName;

        var type = parameter.ParameterType;

        if (FormFileType.Is(type))
        {
            var file = formVar.Invoke("GetFile", QuoteString(name));

            return parameter.Required
                ? Invoke(FormFileBindingType, "Required", file, QuoteString(name))
                : file;
        }

        var files = formVar.Invoke("GetFiles", QuoteString(name));

        IOutputComponent bound = parameter.Required
            ? Invoke(FormFileBindingType, "RequiredMany", files, QuoteString(name))
            : Invoke(FormFileBindingType, "OptionalMany", files);

        var copy =
            type.IsArray ? "ToArray"
            : type.Name is "List" or "IList" or "ICollection" ? "ToList"
            : null;

        if (copy == null)
        {
            return bound;
        }

        IOutputComponent copied = parameter.Required
            ? bound.Invoke(copy)
            : Question(bound).Invoke(copy);

        copied.AddUsingNamespace("System.Linq");

        return copied;
    }

    /// <summary>
    /// A model built from one field per member, each converted the way a parameter of the
    /// member's type would be.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Built in a local and assigned last, because a struct model held in a property of
    /// <c>Parameters</c> cannot have its members set through that property.
    /// </para>
    /// <para>
    /// A member with a setter and an initializer is assigned only when the field was sent, so an
    /// absent field leaves the initializer's value rather than null or zero. Inside that branch the
    /// value is known to be present, which is why it is parsed as required.
    /// </para>
    /// </remarks>
    private static void BindModel(
        RequestParameterInformation parameterInformation,
        BoundModel model,
        MethodDefinition invokeMethod,
        ParameterDefinition context,
        InstanceDefinition parametersVar,
        Func<string, IOutputComponent> field,
        InstanceDefinition? formVar
    )
    {
        var converter = context.Property("KnownServices").Property("StringConverterService");

        // A file member only reaches here on a form, because HRDW007 refuses one on a query
        // string model.
        IOutputComponent Converted(RequestParameterInformation member) =>
            formVar != null && FormFileType.Binds(member.ParameterType)
                ? File(member, formVar)
                : Convert(member, converter, Bang(field(member.BindingName)), member.BindingName);

        var arguments = new List<IOutputComponent>();
        var initializers = new List<Ex>();

        foreach (var member in model.Members)
        {
            if (member.Kind == BoundMemberKind.ConstructorArgument)
            {
                arguments.Add(Converted(member.Value));
            }
            else if (member.Kind == BoundMemberKind.Initializer)
            {
                initializers.Add(
                    Ex.Assign(Ex.Id(member.Value.Name), Ex.Value(Converted(member.Value)))
                );
            }
        }

        IOutputComponent construction =
            initializers.Count == 0
                ? New(parameterInformation.ParameterType, arguments.ToArray<object>())
                : Ex.NewWithInitializer(
                    parameterInformation.ParameterType,
                    arguments.Count == 0 ? null : arguments.Select(Ex.Value).ToList(),
                    initializers.ToArray()
                );

        // Off Name rather than MemberName: MemberName is escaped, and "@eventModel" is not an
        // identifier.
        var modelVar = invokeMethod.Assign(construction).ToVar(parameterInformation.Name + "Model");

        foreach (var member in model.Members)
        {
            var value = member.Value;

            if (member.Kind == BoundMemberKind.Assigned)
            {
                invokeMethod.Assign(Converted(value)).To(modelVar.Property(value.MemberName));
            }
            else if (member.Kind == BoundMemberKind.AssignedWhenSent)
            {
                var itemType = CollectionParameter.ItemType(value.ParameterType);

                // The two readings of "sent" the converter already draws: a collection is absent
                // when no value arrived under its name, and a scalar when that value is empty too.
                var sent =
                    itemType != null
                        ? Ex.GreaterThan(Ex.Value(field(value.BindingName)).Dot("Count"), 0)
                        : Ex.Not(
                            Ex.Call(
                                StringValuesType,
                                "IsNullOrEmpty",
                                Ex.Value(field(value.BindingName))
                            )
                        );

                IOutputComponent converted =
                    itemType != null
                        ? Bang(
                            ConvertMany(
                                value.ParameterType,
                                false,
                                null,
                                converter,
                                field(value.BindingName),
                                value.BindingName,
                                itemType
                            )
                        )
                        : Convert(
                            value.ParameterType,
                            true,
                            null,
                            converter,
                            Bang(field(value.BindingName)),
                            value.BindingName
                        );

                invokeMethod.If(sent).Assign(converted).To(modelVar.Property(value.MemberName));
            }
        }

        invokeMethod.Assign(modelVar).To(parametersVar.Property(parameterInformation.MemberName));
    }

    /// <summary>
    /// A <c>byte[]</c> or <c>Stream</c> body, handed over with nothing between it and the wire.
    /// </summary>
    /// <remarks>
    /// The required check is the one every other body gets. <c>RawBody</c> answers null for a
    /// request that carried no body at all, so a handler declaring the parameter non-nullable
    /// refuses that with the same 400 naming the same parameter, and one declaring it nullable
    /// gets the null.
    /// </remarks>
    private static void BindRawBodyParameter(
        RequestParameterInformation parameterInformation,
        MethodDefinition invokeMethod,
        ParameterDefinition context,
        InstanceDefinition parametersVar
    )
    {
        var isArray = parameterInformation.ParameterType.IsArray;

        IOutputComponent read = Invoke(
            KnownTypes.Requests.RawBody,
            isArray ? "Bytes" : "Body",
            context
        );

        if (isArray)
        {
            read = Await(read);
        }

        IOutputComponent bound = parameterInformation.ParameterType.IsNullable
            ? read
            : Invoke(
                KnownTypes.Requests.RequestBody,
                "Required",
                read,
                QuoteString(parameterInformation.Name)
            );

        invokeMethod.Assign(bound).To(parametersVar.Property(parameterInformation.MemberName));
    }

    private static void BindBodyParameter(
        RequestParameterInformation parameterInformation,
        MethodDefinition invokeMethod,
        ParameterDefinition context,
        InstanceDefinition parametersVar
    )
    {
        // Before the serialization service is even resolved: these two types are the payload, so
        // there is no deserializer to locate and nothing for one to do.
        if (parameterInformation.IsRawBody)
        {
            BindRawBodyParameter(parameterInformation, invokeMethod, context, parametersVar);

            return;
        }

        var getRequiredService = context
            .Property("KnownServices")
            .Property("ContextSerializationService");

        getRequiredService.AddUsingNamespace(
            KnownTypes.Namespace.Microsoft.Extensions.DependencyInjection
        );

        var contentSerializationService = invokeMethod
            .Assign(getRequiredService)
            .ToVar("contentSerializationService");

        var deserializeStatement = Await(
            contentSerializationService.InvokeGeneric(
                "DeserializeRequestBody",
                new[] { parameterInformation.ParameterType },
                context
            )
        );

        // A null-forgiving `!` is a promise to the compiler, not a check. `null` is a valid JSON
        // document, so a caller sending those four bytes deserialized to null, the null reached the
        // handler, and the first dereference was a 500 - the one malformed payload that was not a
        // 400. Where the handler declared the parameter nullable it has said it handles that case,
        // and gets the null.
        IOutputComponent bound = parameterInformation.ParameterType.IsNullable
            ? Parenthesis(deserializeStatement)
            : Invoke(
                KnownTypes.Requests.RequestBody,
                "Required",
                deserializeStatement,
                QuoteString(parameterInformation.Name)
            );

        invokeMethod.Assign(bound).To(parametersVar.Property(parameterInformation.MemberName));
    }
}
