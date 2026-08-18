using System.Globalization;
using System.Net;
using System.Text;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Outputs;
using Hardened.Requests.Abstract.Serializer;

namespace Hardened.Web.Runtime.OpenApi;

/// <summary>
/// Writes the reference page.
/// </summary>
/// <remarks>
/// <para>
/// <b>An output rather than a view.</b> A <c>.cshtml</c> would mean taking RazorBlade and
/// <c>Hardened.Templates.RazorBlade</c> as dependencies for six lines of markup and four
/// substitutions, and every consumer of this package would inherit them.
/// <c>IHardenedResponseOutput</c> anticipates this: "A view is the obvious implementation; a signed
/// file, a server-sent event stream and a protobuf frame are all the same shape."
/// </para>
/// <para>
/// <b>There is no inline script.</b> The document URL travels in a <c>data-</c> attribute, which is
/// the form Scalar's standalone bundle reads, so every value substituted into this page is an HTML
/// attribute value and <see cref="WebUtility.HtmlEncode"/> is the whole escaping story. Writing the
/// URL into a JavaScript string literal instead would need JavaScript escaping on a value that comes
/// from configuration, and getting that subtly wrong is how a docs page becomes an XSS.
/// </para>
/// </remarks>
public sealed class OpenApiUiPage : IHardenedResponseOutput<OpenApiUiModel> {
    private const string ContentTypeValue = "text/html; charset=utf-8";

    /// <summary>
    /// StreamWriter's parameterless UTF8 encoding writes a byte order mark, which would land in the
    /// response body ahead of the markup.
    /// </summary>
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <inheritdoc />
    public bool SupportsContentType(string? accept, IExecutionContext context) {
        var accepted = AcceptedContentTypes.Parse(accept).MediaTypes;

        for (var index = 0; index < accepted.Count; index++) {
            if (MediaType.Matches(accepted[index], ContentTypeValue)) {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public async Task WriteOutput(IExecutionContext context) {
        if (context.Response.ResponseValue is not OpenApiUiModel model) {
            throw new InvalidOperationException(
                $"{nameof(OpenApiUiPage)} needs an {nameof(OpenApiUiModel)} but the response value " +
                $"was {context.Response.ResponseValue?.GetType().Name ?? "null"}.");
        }

        var page = Utf8NoBom.GetBytes(Render(model));

        context.Response.ContentType = ContentTypeValue;
        context.Response.Headers[KnownHeaders.ContentLength] =
            page.Length.ToString(CultureInfo.InvariantCulture);

        await context.Response.Body.WriteAsync(page, 0, page.Length);
    }

    /// <summary>
    /// The page, as a pure function of its model.
    /// </summary>
    private static string Render(OpenApiUiModel model) {
        var builder = new StringBuilder(512);

        builder
            .Append("<!doctype html>\n<html lang=\"en\">\n<head>\n")
            .Append("<meta charset=\"utf-8\">\n")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n")
            .Append("<title>").Append(Encode(model.Title)).Append("</title>\n")
            .Append("</head>\n<body>\n")
            .Append("<script id=\"api-reference\" data-url=\"")
            .Append(Encode(model.DocumentPath))
            .Append("\"></script>\n")
            .Append("<script src=\"").Append(Encode(model.ScriptUrl)).Append('"');

        // Only when there is one to state. A same-origin script does not need integrity, and an
        // empty attribute is not "no integrity" - it is a hash nothing matches, which fails the
        // script closed.
        if (!string.IsNullOrEmpty(model.ScriptIntegrity)) {
            builder
                .Append(" integrity=\"").Append(Encode(model.ScriptIntegrity!)).Append('"')
                .Append(" crossorigin=\"anonymous\"");
        }

        return builder.Append("></script>\n</body>\n</html>\n").ToString();
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
