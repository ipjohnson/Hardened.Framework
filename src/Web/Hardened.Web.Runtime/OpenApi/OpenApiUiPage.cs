using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
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
    public bool SupportsContentType(string? accept, IExecutionContext context) =>
        MediaType.Accepts(accept, ContentTypeValue);

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
            .Append("</head>\n<body>\n");

        // The mount point the initialiser names. Only the plugin form needs one: the standalone
        // bundle finds its own script tag and renders in place.
        if (model.MessagePackScriptUrl != null) {
            builder.Append("<div id=\"app\"></div>\n");
        }
        else {
            builder
                .Append("<script id=\"api-reference\" data-url=\"")
                .Append(Encode(model.DocumentPath))
                .Append("\"></script>\n");
        }

        builder.Append("<script src=\"").Append(Encode(model.ScriptUrl)).Append('"');

        // Only when there is one to state. A same-origin script does not need integrity, and an
        // empty attribute is not "no integrity" - it is a hash nothing matches, which fails the
        // script closed.
        if (!string.IsNullOrEmpty(model.ScriptIntegrity)) {
            builder
                .Append(" integrity=\"").Append(Encode(model.ScriptIntegrity!)).Append('"')
                .Append(" crossorigin=\"anonymous\"");
        }

        builder.Append("></script>\n");

        if (model.MessagePackScriptUrl != null) {
            builder
                .Append("<script src=\"").Append(Encode(model.MessagePackScriptUrl)).Append("\"></script>\n")
                .Append(Initialiser(model.DocumentPath));
        }

        return builder.Append("</body>\n</html>\n").ToString();
    }

    /// <summary>
    /// The <c>createApiReference</c> call, which is the only form that takes a plugin.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Written only for a page that asked for the plugin.</b> A plugin is a function, and a
    /// function does not fit in the <c>data-</c> attribute the standalone bundle reads - so
    /// installing one means initialising in script. A page that installs none keeps the attribute
    /// form it has always had, and with it the property that every value it substitutes is an HTML
    /// attribute and nothing on the page is inline script. Opting in is opting into that too, which
    /// is what a <c>script-src</c> policy naming no <c>'unsafe-inline'</c> would notice.
    /// </para>
    /// <para>
    /// The document URL goes through <see cref="JsonEncodedText"/>, which is the JavaScript half of
    /// what <see cref="WebUtility.HtmlEncode"/> is above: a JSON string is a JavaScript string, so
    /// one function covers the whole of what a configured value can do to the syntax around it.
    /// Not <c>JsonSerializer</c>, which carries the trimming and AOT annotations this assembly is
    /// built to keep clear of, and would be reflection over a string.
    /// </para>
    /// <para>
    /// The decoder is the global the MessagePack UMD build installs, rather than a dynamic import,
    /// because the script tag above has already loaded it by the time this runs.
    /// </para>
    /// </remarks>
    private static string Initialiser(string documentPath) =>
        $$"""
          <script>
          Scalar.createApiReference('#app', {
            url: "{{JsonEncodedText.Encode(documentPath)}}",
            plugins: [
              () => {
                // What the operation declares for the body about to be decoded, recorded as the
                // response arrives: decode() is handed the bytes and a media type and nothing else.
                let declared = null;

                const deref = (node, document) => {
                  for (let hop = 0; node && node.$ref && hop < 10; hop++) {
                    node = node.$ref.replace(/^#\//, '').split('/').reduce(
                      (at, key) => at && at[key.replace(/~1/g, '/').replace(/~0/g, '~')], document);
                  }

                  return node;
                };

                // A keyed member is a position on the wire and a name in the document, so this
                // walks the decoded value beside the schema and puts the names back. A named member
                // arrives as one already, and is walked for the keyed types underneath it.
                const name = (value, node, document) => {
                  const schema = deref(node, document);

                  if (!schema) {
                    return value;
                  }

                  if (Array.isArray(value) && schema.items) {
                    return value.map((item) => name(item, schema.items, document));
                  }

                  const properties = schema.properties;

                  if (!properties) {
                    return value;
                  }

                  if (Array.isArray(value)) {
                    const object = {};

                    for (const [member, property] of Object.entries(properties)) {
                      const index = property['x-message-pack-index'];

                      // One member without an index and the positions are not the document's to
                      // read. The array is answered as it arrived rather than half named.
                      if (index === undefined || index >= value.length) {
                        return value;
                      }

                      object[member] = name(value[index], property, document);
                    }

                    return object;
                  }

                  if (value && typeof value === 'object') {
                    return Object.fromEntries(Object.entries(value).map(
                      ([member, item]) => [member, name(item, properties[member], document)]));
                  }

                  return value;
                };

                return {
                  name: 'hardened-messagepack',
                  extensions: [],
                  apiClientPlugins: [
                    {
                      hooks: {
                        // Before the request goes out, not after the response arrives. decode()
                        // runs first of those two, so a schema recorded on the way back is always
                        // one response late - the first body renders unnamed and every one after it
                        // is named from the previous operation's schema.
                        //
                        // Which status answered is not known this early, so every representation
                        // the operation declares is recorded and decode() takes the one that fits.
                        // An operation declaring one - which is all of them until a refusal carries
                        // a different shape - has nothing to choose between.
                        beforeRequest: ({ operation, document }) => {
                          const content = Object.values(operation?.responses ?? {}).map(
                            (response) => deref(response, document)?.content ?? {});

                          declared = {
                            document,
                            schemas: content.map(
                              (media) => (media['application/x-msgpack']
                                ?? media['application/msgpack'])?.schema).filter(Boolean),
                          };
                        },
                      },
                      responseBody: [
                        {
                          mimeTypes: ['application/msgpack', 'application/x-msgpack'],
                          decode: async (buffer) => {
                            const value = MessagePack.decode(new Uint8Array(buffer));

                            // name() hands back what it was given when the schema does not describe
                            // the value, so identity is what says a candidate fitted.
                            const named = (declared?.schemas ?? []).map(
                              (schema) => name(value, schema, declared.document)).find(
                                (candidate) => candidate !== value);

                            return JSON.stringify(named ?? value, null, 2);
                          },
                          language: 'json',
                        },
                      ],
                    },
                  ],
                };
              },
            ],
          });
          </script>

          """;

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
