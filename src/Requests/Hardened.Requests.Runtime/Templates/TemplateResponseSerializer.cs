using System.Text;
using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Abstract.Templates;

namespace Hardened.Requests.Runtime.Templates;

/// <summary>
/// Renders a response that carries a view.
/// </summary>
/// <remarks>
/// <para>
/// There is no engine any more, and no template registry. A template renders itself: the generated
/// handler puts a factory on the response, this builds the view, hands it the model and the
/// context, and asks it to write. What used to be a name resolved through a dictionary of
/// descriptors, ordered across several engines, is a delegate and a virtual call.
/// </para>
/// <para>
/// <b>Registered with <c>Add</c>, not <c>Try</c>.</b> <c>Try</c> emits <c>TryAddSingleton</c>,
/// which is first-wins for a service type - so on an interface with several implementations it
/// means "do not register if anyone else already did". Registered that way this class never
/// entered the container at all: the JSON serializer got there first, the locator only ever saw
/// one candidate, and <c>/fortunes</c> answered with a JSON-serialized model while the template
/// never ran. Nothing reported it, because a no-op registration is not an error.
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

    /// <summary>
    /// StreamWriter's parameterless UTF8 encoding writes a byte order mark, which lands in the
    /// response body ahead of the markup and is visible in the rendered page.
    /// </summary>
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>
    /// Never the fallback. A response with no view is not this serializer's business, and claiming
    /// otherwise would answer every unmatched request with an exception about a template that was
    /// never chosen.
    /// </summary>
    public bool IsDefaultSerializer => false;

    public int Order => (int)ResponseSerializerOrder.Template;

    public bool CanProduce(string mediaType, IExecutionContext context) {
        var template = Resolve(context);

        // The media type comes from the template rather than from this class: a view on an HTML
        // base and one on a plain-text base are rendered the same way and answer differently.
        return template != null && MediaType.Matches(mediaType, template.ContentType);
    }

    public async Task SerializeResponse(IExecutionContext context) {
        var template = Resolve(context);

        if (template == null) {
            // Reachable when a filter clears the template after selection ran.
            throw new InvalidOperationException(
                "TemplateResponseSerializer was selected for a response with no template.");
        }

        template.Attach(context.Response.ResponseValue, context);

        // Only when the handler has not already chosen one. Checked for empty as well as null
        // because the ASP.NET Core host coerces a null assignment to "", so a response that has
        // been touched and left unset reads back as empty rather than null.
        if (string.IsNullOrEmpty(context.Response.ContentType)) {
            context.Response.ContentType = template.ContentType;
        }

        // leaveOpen: the response body outlives this render - headers, trailers and the host's own
        // completion all still need it.
        await using var writer = new StreamWriter(context.Response.Body, Utf8NoBom, -1, true);

        await template.RenderAsync(writer, context.CancellationToken);

        await writer.FlushAsync();
    }

    /// <summary>
    /// The view for this response, built once. Negotiation asks what a template produces once per
    /// media type the client listed, and building one per question would allocate a view for a
    /// request that may not even be rendered as one.
    /// </summary>
    private static IHardenedTemplate? Resolve(IExecutionContext context) {
        var response = context.Response;

        if (response.Template != null) {
            return response.Template;
        }

        var factory = response.TemplateFactory;

        if (factory == null) {
            return null;
        }

        response.Template = factory(context);

        return response.Template;
    }
}
