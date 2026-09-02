using CSharpAuthor;
using static CSharpAuthor.SyntaxHelpers;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;

namespace Hardened.SourceGenerator.Requests;

public static class InvokeMethodCodeGenerator {
    public static void Implement(RequestHandlerModel requestHandlerModel, ClassDefinition classDefinition) {
        var invokeMethod = classDefinition.AddMethod("InvokeMethod");

        invokeMethod.Modifiers = ComponentModifier.Private | ComponentModifier.Static;

        if (requestHandlerModel.ResponseInformation.IsAsync) {
            invokeMethod.Modifiers |= ComponentModifier.Async;
            invokeMethod.SetReturnType(typeof(Task));
        }

        var context = invokeMethod.AddParameter(KnownTypes.Requests.IExecutionContext, "context");
        var controller = invokeMethod.AddParameter(requestHandlerModel.ControllerType, "controller");

        InvokeDefinition invoke = controller.Invoke(requestHandlerModel.HandlerMethod);

        ProcessArguments(requestHandlerModel, invoke, invokeMethod);

        IOutputComponent invokeStatement = invoke;

        if (requestHandlerModel.ResponseInformation.IsAsync) {
            invokeStatement = Await(invokeStatement);
        }

        AssignOutputFactory(requestHandlerModel, invokeMethod, context);
        AssignRawContentType(requestHandlerModel, invokeMethod, context);

        var cases = UnionResponseSelector.Decode(requestHandlerModel.ResponseInformation.UnionCases);

        if (cases.Count > 0) {
            EmitResponseSetDispatch(invokeMethod, invokeStatement, context, cases);
        }
        else if (requestHandlerModel.ResponseInformation.ReturnType != null &&
                 requestHandlerModel.ResponseInformation.ReturnType.Name != typeof(void).Name) {
            EmitSingleResponseDispatch(
                invokeMethod, invokeStatement, context,
                requestHandlerModel.ResponseInformation.ReturnTypeProvidesHeaders);
        }
        else {
            invokeMethod.AddIndentedStatement(invokeStatement);
        }
    }

    /// <summary>
    /// The glue for a handler that returns one thing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The returned value goes to <c>ResponseValue</c>, and it applies its own headers first if it
    /// has any. A response set reaches <c>ApplyHeaders</c> through its switch, and this path had no
    /// switch to reach it through - so a type carrying a header answered without one, which is what
    /// a Smithy output binding <c>@httpHeader</c> and a hand-written <c>Created&lt;T&gt;</c> both
    /// looked like in throws mode.
    /// </para>
    /// <para>
    /// A run-time test rather than a generated decision, because the generator does not always know:
    /// a code-first handler can return any type, including one an application wrote and implemented
    /// the interface on. It is a single <c>isinst</c> against a value already in hand.
    /// </para>
    /// </remarks>
    private static void EmitSingleResponseDispatch(
        MethodDefinition invokeMethod,
        IOutputComponent invokeStatement,
        ParameterDefinition context,
        bool providesHeaders) {
        if (!providesHeaders) {
            invokeMethod.Assign(invokeStatement).To(context.Property("Response.ResponseValue"));

            return;
        }

        var result = invokeMethod.Assign(invokeStatement).ToVar(ResultVariable);

        invokeMethod.AddIndentedStatement(
            CodeOutputComponent.Get(
                "if (" + ResultVariable + " is global::Hardened.Requests.Abstract.Responses." +
                "IProvidesResponseHeaders __headerProvider) __headerProvider.ApplyHeaders(context.Response.Headers)"));

        invokeMethod.Assign(result).To(context.Property("Response.ResponseValue"));
    }

    /// <summary>
    /// The glue for a handler that returns a declared response set.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The payload needs no transformation: <c>IExecutionResponse.ResponseValue</c> is already
    /// <c>object?</c> and a union's <c>Value</c> is already <c>object?</c>, so it is assigned once
    /// before the switch rather than in every arm. What the switch decides is the status, the
    /// headers, and whether anything is serialized - the three things the case type knows and the
    /// pipeline does not.
    /// </para>
    /// <para>
    /// A type switch rather than a chain of <c>is</c> checks, so the compiler orders the tests and
    /// reports an unreachable arm. Case types cannot be assignable to one another - the built-in
    /// ones are sealed and the diagnostics reject a set where they are - so arm order carries no
    /// meaning and the emitted order is the declared one, which is what makes the generated file
    /// readable against the signature.
    /// </para>
    /// <para>
    /// <c>default</c> answers 500. It is reached when <c>Value</c> is null, which
    /// <c>return default;</c> produces and which compiles - a handler that declared no case has
    /// stated nothing, and guessing a success status for it would send a caller an empty body under
    /// a 200.
    /// </para>
    /// <para>
    /// Built through <c>SwitchBlockDefinition</c> rather than composed as text. Only the case label
    /// is a raw component, because a case type is a fully-qualified name rather than a type this
    /// assembly can reference - the same reason <c>OneOfEmitter</c> emits its constructors that way.
    /// Everything else is a statement, so the braces and the indentation are the author's problem
    /// rather than this file's.
    /// </para>
    /// </remarks>
    private static void EmitResponseSetDispatch(
        MethodDefinition invokeMethod,
        IOutputComponent invokeStatement,
        ParameterDefinition context,
        IReadOnlyList<UnionCaseModel> cases) {
        var result = invokeMethod.Assign(invokeStatement).ToVar(ResultVariable);

        var payload = result.Property(ValueProperty);

        invokeMethod.Assign(payload).To(context.Property("Response.ResponseValue"));

        var switchBlock = invokeMethod.Switch(payload);

        for (var i = 0; i < cases.Count; i++) {
            var unionCase = cases[i];

            // A named binding where an arm reads it - to apply headers, or to take the body off a
            // case that wraps one. `case T _:` is a declaration pattern with
            // a discard, which has been legal since C# 7 - lower than the bare type pattern `case
            // T:` a reader might reach for, and the difference matters because this is a consumer's
            // build rather than ours. The alternative was naming every binding and discarding the
            // unread ones, which put a `_ = __case0;` line in most arms of every handler.
            var reads = unionCase.AppliesHeaders || (unionCase.CarriesBody && unionCase.HasBody);
            var binding = reads ? CaseVariable + i : "_";

            var caseBlock = switchBlock.AddCase(
                CodeOutputComponent.Get(unionCase.TypeName + " " + binding));

            caseBlock.Assign(CodeOutputComponent.Get(unionCase.Status.ToString()))
                .To(context.Property("Response.Status"));

            if (unionCase.AppliesHeaders) {
                caseBlock.AddIndentedStatement(
                    CodeOutputComponent.Get(binding)
                        .Invoke("ApplyHeaders", context.Property("Response.Headers")));
            }

            // Overrides the assignment made before the switch, for a case whose body is one of its
            // members rather than the case itself - Created<T> and the generic problem types.
            // Sending the wrapper would nest the caller's payload under a member and ship the
            // wrapper's own fields beside it.
            if (unionCase.CarriesBody && unionCase.HasBody) {
                caseBlock
                    .Assign(CodeOutputComponent.Get(
                        "((global::Hardened.Requests.Abstract.Responses.ICarriesResponseBody)" +
                        binding + ").Body"))
                    .To(context.Property("Response.ResponseValue"));
            }

            if (!unionCase.HasBody) {
                caseBlock.Assign(CodeOutputComponent.Get("false"))
                    .To(context.Property("Response.ShouldSerialize"));
            }

            caseBlock.Break();
        }

        var fallback = switchBlock.AddDefault();

        fallback.Assign(CodeOutputComponent.Get("500")).To(context.Property("Response.Status"));
        fallback.Assign(CodeOutputComponent.Get("false"))
            .To(context.Property("Response.ShouldSerialize"));
        fallback.Break();
    }

    private const string ResultVariable = "__response";

    private const string CaseVariable = "__case";

    private const string ValueProperty = "Value";

    /// <summary>
    /// Puts the handler's <c>[Output&lt;T&gt;]</c> factory on the response, when it declares one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The build-time and run-time halves of an output meet here and nowhere else: the type
    /// is known when the handler is generated, and the thing that renders it runs per request.
    /// </para>
    /// <para>
    /// Assigned before the handler is invoked rather than after, so a handler that wants to choose
    /// its own view - mobile against desktop, an A/B test, an error view - can overwrite it through
    /// the response it was handed. That is the dynamic selection a template name allowed, kept, and
    /// typed.
    /// </para>
    /// </remarks>
    private static void AssignOutputFactory(
        RequestHandlerModel requestHandlerModel, MethodDefinition invokeMethod, ParameterDefinition context) {
        if (requestHandlerModel.ResponseInformation.OutputType == null) {
            return;
        }

        invokeMethod
            .Assign(CodeOutputComponent.Get(OutputFactoryGenerator.FactoryField))
            .To(context.Property("Response.OutputFactory"));
    }

    /// <summary>
    /// Commits the response to the content type a handler's <c>[RawResponse]</c> declares.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A plain assignment, in the same place and for the same reason as the template name. This
    /// used to be a <c>DefaultOutput</c> closure built in the constructor, which
    /// <c>ContextSerializationService</c> consulted ahead of every serializer - so the attribute did
    /// not participate in selection, it pre-empted it. <c>RawResponseSerializer</c> now claims a
    /// response that has committed to a content type, through the same locator as everything else.
    /// </para>
    /// <para>
    /// Assigned before the handler runs, so a handler can overwrite it to choose a content type per
    /// request - which also makes forcing available without the attribute at all.
    /// </para>
    /// </remarks>
    private static void AssignRawContentType(
        RequestHandlerModel requestHandlerModel, MethodDefinition invokeMethod, ParameterDefinition context) {
        var contentType = requestHandlerModel.ResponseInformation.RawResponseContentType;

        if (string.IsNullOrEmpty(contentType)) {
            return;
        }

        invokeMethod.Assign(QuoteString(contentType!)).To(context.Property("Response.ContentType"));
    }

    private static void ProcessArguments(RequestHandlerModel requestHandlerModel, InvokeDefinition invoke,
        MethodDefinition invokeMethod) {
        if (requestHandlerModel.RequestParameterInformationList.Count > 0) {
            var parameters = invokeMethod.AddParameter(
                InvokeClassGenerator.ParametersType(requestHandlerModel), "parameters");

            foreach (var parameterInformation in requestHandlerModel.RequestParameterInformationList) {
                invoke.AddArgument(parameters.Property(parameterInformation.Name));
            }
        }
    }
}