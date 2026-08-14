namespace Hardened.Requests.Abstract.Serializer;

/// <summary>
/// An <c>Accept</c> header parsed into the media types a client will take, most preferred first.
/// </summary>
/// <remarks>
/// <para>
/// Parsed once per response, in <c>ISerializationLocatorService</c>, and handed to serializers as an
/// argument. Serializers never see the raw header: they are asked whether they can produce a
/// specific media type, so no serializer implements ranking or wildcard handling of its own.
/// </para>
/// <para>
/// <b>Parameters are discarded, including q.</b> Preference comes from the order types are listed
/// in, which is how well-formed clients write the header - all three of TechEmpower's, for one,
/// list their preferred type first and use q only to restate it. A header that contradicts its own
/// order, <c>text/html;q=0.5, application/json;q=0.9</c>, resolves to <c>text/html</c> here. That is
/// a decision rather than an oversight: honouring q means sorting, sorting means allocating, and
/// nothing observed in practice sends it. Adding it later changes this file and nothing else.
/// </para>
/// </remarks>
public sealed class AcceptedContentTypes {
    /// <summary>
    /// What a request with no <c>Accept</c> header accepts: anything. Shared rather than allocated
    /// per request, because it is the common case - curl, most HTTP clients, and the test host all
    /// send no header or <c>*/*</c>.
    /// </summary>
    public static readonly AcceptedContentTypes Any = new(new[] { MediaType.Any });

    private readonly IReadOnlyList<string> _mediaTypes;

    private AcceptedContentTypes(IReadOnlyList<string> mediaTypes) {
        _mediaTypes = mediaTypes;
    }

    /// <summary>The media types, most preferred first.</summary>
    public IReadOnlyList<string> MediaTypes => _mediaTypes;

    public static AcceptedContentTypes Parse(string? acceptHeader) {
        if (string.IsNullOrWhiteSpace(acceptHeader)) {
            return Any;
        }

        var trimmed = acceptHeader!.Trim();

        // The single most common header there is, and it needs no parsing at all.
        if (trimmed == MediaType.Any) {
            return Any;
        }

        var mediaTypes = new List<string>();

        foreach (var entry in trimmed.Split(',')) {
            // Everything from the first ';' is parameters - q, charset, version tags such as
            // ";v=b3". None of it participates in matching.
            var semicolon = entry.IndexOf(';');
            var mediaType = (semicolon < 0 ? entry : entry.Substring(0, semicolon)).Trim();

            if (mediaType.Length > 0) {
                mediaTypes.Add(mediaType);
            }
        }

        return mediaTypes.Count == 0 ? Any : new AcceptedContentTypes(mediaTypes);
    }
}
