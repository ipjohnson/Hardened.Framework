using System.Text;
using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Templates;

namespace Hardened.Templates.RazorBlade.Impl;

/// <summary>
/// Renders RazorBlade templates by name.
/// </summary>
/// <remarks>
/// Holds no Razor machinery of its own. RazorBlade compiles .cshtml to C# at build time inside the
/// application's compilation, so by the time this runs a template is an ordinary class and this is
/// a dictionary lookup and a call.
/// </remarks>
/// <remarks>
/// <para>
/// <c>Add</c> rather than <c>Try</c>, because <c>ITemplateEngine</c> is resolved as a set.
/// <c>Try</c> emits <c>TryAddSingleton</c>, which is first-wins per service type, so registering
/// that way would mean an application adding a second engine silently got only one of them - and
/// which one would depend on registration order. It also made <c>ITemplateEngine</c>'s own
/// contract - engines tested in order, so an application can register one for a subset of its
/// views - describe something that could not happen.
/// </para>
/// </remarks>
[SingletonService(Using = RegistrationType.Add)]
public class RazorBladeTemplateEngine : ITemplateEngine {
    /// <summary>
    /// StreamWriter's parameterless UTF8 encoding writes a byte order mark, which lands in the
    /// response body ahead of the markup and is visible in the rendered page.
    /// </summary>
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private readonly Dictionary<string, RazorBladeTemplateDescriptor> _templates;

    public RazorBladeTemplateEngine(IEnumerable<IRazorBladeTemplateSource> sources) {
        _templates = new Dictionary<string, RazorBladeTemplateDescriptor>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sources) {
            foreach (var descriptor in source.Templates) {
                _templates[descriptor.Name] = descriptor;
            }
        }
    }

    public bool CanRender(string templateName) => _templates.ContainsKey(templateName);

    public async Task RenderAsync(string templateName, object? model, IExecutionContext context) {
        if (!_templates.TryGetValue(templateName, out var descriptor)) {
            throw new InvalidOperationException(
                $"No RazorBlade template named '{templateName}'. " +
                $"Known templates: {string.Join(", ", _templates.Keys.OrderBy(key => key, StringComparer.Ordinal))}");
        }

        var template = descriptor.Create(model);

        // Only when the handler has not already chosen one. Checked for empty as well as null
        // because the ASP.NET Core host coerces a null assignment to "", so a response that has
        // been touched and left unset reads back as empty rather than null.
        if (string.IsNullOrEmpty(context.Response.ContentType)) {
            context.Response.ContentType = descriptor.ContentType;
        }

        // leaveOpen: the response body outlives this render - headers, trailers and the host's own
        // completion all still need it.
        await using var writer = new StreamWriter(context.Response.Body, Utf8NoBom, -1, true);

        await template.RenderAsync(writer, context.CancellationToken);

        await writer.FlushAsync();
    }
}
