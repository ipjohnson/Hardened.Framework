using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Abstract.Templates;

namespace Hardened.Requests.Runtime.Templates;

/// <summary>
/// Routes a response carrying a template name to whichever <see cref="ITemplateEngine"/> claims it.
/// </summary>
/// <remarks>
/// <para>
/// Shaped as an <see cref="IResponseSerializer"/> because that is what it does, but
/// <b>not resolved through the locator</b>. <c>ContextSerializationService</c> holds one of these
/// directly and asks it before it asks the locator anything.
/// </para>
/// <para>
/// That is not a stylistic choice. The locator returns the first registered serializer that claims
/// the context, over a list it reverses, so a template response whose request also carries
/// <c>Accept: application/json</c> resolves on registration order - and both candidates are
/// registered by this assembly's own module, so there is no ordering an application could pick to
/// make the template win. Registering it as one serializer among the others produced exactly that:
/// <c>/fortunes</c> returned a JSON-serialized model with a content type of application/json, and
/// the template never ran.
/// </para>
/// </remarks>
[SingletonService(Using = RegistrationType.Try)]
public class TemplateResponseSerializer : ITemplateResponseSerializer {
    private readonly ITemplateEngine[] _engines;

    public TemplateResponseSerializer(IEnumerable<ITemplateEngine> engines) {
        // Reversed for the same reason SerializationLocatorService reverses: an engine registered
        // by the application should be tested before one the framework registered.
        _engines = engines.Reverse().ToArray();
    }

    public bool CanProcessContext(IExecutionContext context) {
        var templateName = context.Response.TemplateName;

        return templateName != null && FindEngine(templateName) != null;
    }

    public Task SerializeResponse(IExecutionContext context) {
        var templateName = context.Response.TemplateName;

        if (templateName == null) {
            throw new InvalidOperationException(
                "TemplateResponseSerializer was selected for a response with no TemplateName.");
        }

        var engine = FindEngine(templateName);

        if (engine == null) {
            // Reachable when a handler assigns TemplateName after CanProcessContext ran.
            throw new InvalidOperationException(
                $"No template engine can render '{templateName}'.");
        }

        return engine.RenderAsync(templateName, context.Response.ResponseValue, context);
    }

    private ITemplateEngine? FindEngine(string templateName) {
        for (var i = 0; i < _engines.Length; i++) {
            if (_engines[i].CanRender(templateName)) {
                return _engines[i];
            }
        }

        return null;
    }
}
