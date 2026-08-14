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
/// <b>Registered with <c>Add</c>, not <c>Try</c>.</b> <c>Try</c> emits <c>TryAddSingleton</c>, which
/// is first-wins for a service type - so on an interface with several implementations it means "do
/// not register if anyone else already did". Registered that way this class never entered the
/// container at all: the JSON serializer got there first, the locator only ever saw one candidate,
/// and <c>/fortunes</c> answered with a JSON-serialized model while the template never ran. Nothing
/// reported it, because a no-op registration is not an error.
/// </para>
/// <para>
/// Ordered at <see cref="ResponseSerializerOrder.Template"/> so it is asked before JSON. A browser
/// sends <c>Accept: text/html, ... , */*</c>, which the JSON serializer does not claim, but an API
/// client asking for JSON against a templated route would otherwise get the model serialized.
/// Ordering states the intent instead of leaving it to how two class names happen to sort.
/// </para>
/// </remarks>
[SingletonService(Using = RegistrationType.Add)]
public class TemplateResponseSerializer : IResponseSerializer {
    private readonly ITemplateEngine[] _engines;

    public TemplateResponseSerializer(IEnumerable<ITemplateEngine> engines) {
        // Reversed for the same reason SerializationLocatorService reverses: an engine registered
        // by the application should be tested before one the framework registered.
        _engines = engines.Reverse().ToArray();
    }

    /// <summary>
    /// Never the fallback. A response with no template name is not this serializer's business, and
    /// claiming otherwise would answer every unmatched request with an exception about a template
    /// name that was never set.
    /// </summary>
    public bool IsDefaultSerializer => false;

    public int Order => (int)ResponseSerializerOrder.Template;

    public bool CanProduce(string mediaType, IExecutionContext context) {
        var templateName = context.Response.TemplateName;

        if (templateName == null) {
            return false;
        }

        // The media type comes from the template rather than from this class: an HtmlTemplate view
        // and a PlainTextTemplate view are both rendered by the same engine and answer differently.
        return MediaType.Matches(mediaType, FindEngine(templateName)?.ContentTypeFor(templateName));
    }

    public Task SerializeResponse(IExecutionContext context) {
        var templateName = context.Response.TemplateName;

        if (templateName == null) {
            throw new InvalidOperationException(
                "TemplateResponseSerializer was selected for a response with no TemplateName.");
        }

        var engine = FindEngine(templateName);

        if (engine == null) {
            // Reachable when a handler assigns TemplateName after selection ran.
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
