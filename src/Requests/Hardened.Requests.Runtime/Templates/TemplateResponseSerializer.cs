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
/// A serializer rather than a branch in <c>ContextSerializationService</c>, because the locator
/// already performs exactly this dispatch: ask each candidate whether it can handle the context and
/// take the first that says yes. Adding a fourth arm to that method would have duplicated it.
/// </para>
/// <para>
/// <b>Ordering matters and is not free.</b> <c>SerializationLocatorService</c> returns the first
/// serializer whose <c>CanProcessContext</c> is true, over a list it reverses so that later
/// registrations are tested first. A request with both <c>Accept: application/json</c> and a
/// template name therefore goes to whichever of the two was registered last. Registering a template
/// module after the core module gives the intended order, which
/// <c>TemplateResponseSerializerTests</c> pins rather than trusting.
/// </para>
/// </remarks>
[SingletonService(Using = RegistrationType.Try)]
public class TemplateResponseSerializer : IResponseSerializer {
    private readonly ITemplateEngine[] _engines;

    public TemplateResponseSerializer(IEnumerable<ITemplateEngine> engines) {
        // Reversed for the same reason SerializationLocatorService reverses: an engine registered
        // by the application should be tested before one the framework registered.
        _engines = engines.Reverse().ToArray();
    }

    /// <summary>
    /// Never the fallback. A response with no template name is not this serializer's business, and
    /// claiming otherwise would put it ahead of JSON for every unmatched request.
    /// </summary>
    public bool IsDefaultSerializer => false;

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
