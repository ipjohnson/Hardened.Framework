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

        AssignTemplateName(requestHandlerModel, invokeMethod, context);
        AssignRawContentType(requestHandlerModel, invokeMethod, context);

        if (requestHandlerModel.ResponseInformation.ReturnType != null && 
            requestHandlerModel.ResponseInformation.ReturnType.Name != typeof(void).Name) {
            invokeMethod.Assign(invokeStatement).To(context.Property("Response.ResponseValue"));
        }
        else {
            invokeMethod.AddIndentedStatement(invokeStatement);
        }
    }

    /// <summary>
    /// Puts the handler's <c>[Template]</c> name on the response, when it declares one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The attribute is an annotation and nothing more - the engine that used to read it was
    /// removed in #101, and the renderer that will replace it does not exist yet. What this does is
    /// make the annotation <em>reachable</em>: the name is known at build time and the thing that
    /// will want it runs per request, so the generated handler is the only place the two meet.
    /// </para>
    /// <para>
    /// Without it <c>IExecutionResponse.TemplateName</c> is decoration. That is exactly the state
    /// #101 found the property in - "never assigned, never read" - and the reason it was deleted
    /// along with the engine. Re-adding the property and not assigning it would earn the same
    /// verdict a second time.
    /// </para>
    /// <para>
    /// Assigned before the handler is invoked rather than after, so a handler that wants to choose
    /// its own view can overwrite it through the response it was handed.
    /// </para>
    /// </remarks>
    private static void AssignTemplateName(
        RequestHandlerModel requestHandlerModel, MethodDefinition invokeMethod, ParameterDefinition context) {
        var templateName = requestHandlerModel.ResponseInformation.TemplateName;

        if (string.IsNullOrEmpty(templateName)) {
            return;
        }

        invokeMethod.Assign(QuoteString(templateName!)).To(context.Property("Response.TemplateName"));
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
            var parameters = invokeMethod.AddParameter(InvokeClassGenerator.GenericParameters, "parameters");

            foreach (var parameterInformation in requestHandlerModel.RequestParameterInformationList) {
                invoke.AddArgument(parameters.Property(parameterInformation.Name));
            }
        }
    }
}