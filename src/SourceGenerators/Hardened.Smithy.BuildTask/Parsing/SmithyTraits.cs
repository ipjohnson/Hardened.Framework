namespace Hardened.Smithy.BuildTask.Parsing;

/// <summary>
/// The prelude traits, and what this front end does with each.
/// </summary>
/// <remarks>
/// <para>
/// Smithy 2.0's prelude is about seventy-seven traits and twenty-two shape types. That is the whole
/// language, which is what makes this table closable rather than something to keep extending.
/// </para>
/// <para>
/// <b>The rule that decides which list a trait belongs in:</b> ignoring a trait is safe only if the
/// generated server still behaves the way the model says. Everything else is a degrade with a
/// warning or a hard error. Never a silent skip.
/// </para>
/// <para>
/// <b><see cref="Ignorable"/> is an allowlist, not a fallback.</b> A blanket ignore would mean every
/// trait Smithy adds in future, and every trait a dependency brings, silently changes nothing. An
/// explicit list makes an unrecognised trait surface as "this model uses X and we do not model it",
/// which costs one line to resolve in either direction and is the difference between a bounded
/// feature set and unbounded silent drift.
/// </para>
/// </remarks>
internal static class SmithyTraits {

    internal const string Trait = "smithy.api#trait";

    internal const string Http = "smithy.api#http";
    internal const string HttpLabel = "smithy.api#httpLabel";
    internal const string HttpQuery = "smithy.api#httpQuery";
    internal const string HttpHeader = "smithy.api#httpHeader";
    internal const string HttpPayload = "smithy.api#httpPayload";
    internal const string HttpError = "smithy.api#httpError";
    internal const string HttpResponseCode = "smithy.api#httpResponseCode";
    internal const string HttpPrefixHeaders = "smithy.api#httpPrefixHeaders";
    internal const string HttpQueryParams = "smithy.api#httpQueryParams";

    internal const string Error = "smithy.api#error";
    internal const string Required = "smithy.api#required";
    internal const string Default = "smithy.api#default";
    internal const string ClientOptional = "smithy.api#clientOptional";
    internal const string Documentation = "smithy.api#documentation";
    internal const string Deprecated = "smithy.api#deprecated";
    internal const string Title = "smithy.api#title";
    internal const string Tags = "smithy.api#tags";
    internal const string EnumValue = "smithy.api#enumValue";
    internal const string JsonName = "smithy.api#jsonName";
    internal const string TimestampFormat = "smithy.api#timestampFormat";
    internal const string MediaType = "smithy.api#mediaType";
    internal const string Sparse = "smithy.api#sparse";
    internal const string Streaming = "smithy.api#streaming";
    internal const string Length = "smithy.api#length";
    internal const string Range = "smithy.api#range";
    internal const string Pattern = "smithy.api#pattern";
    internal const string UniqueItems = "smithy.api#uniqueItems";
    internal const string Readonly = "smithy.api#readonly";
    internal const string Idempotent = "smithy.api#idempotent";
    internal const string Input = "smithy.api#input";
    internal const string Output = "smithy.api#output";
    internal const string Private = "smithy.api#private";
    internal const string Internal = "smithy.api#internal";
    internal const string Mixin = "smithy.api#mixin";

    /// <summary>
    /// Traits read into the IR.
    /// </summary>
    /// <remarks>
    /// Membership here is what stops the unknown-trait pass reporting a trait the parser already
    /// acted on. It is deliberately separate from the parser's own reads: a trait added to the
    /// parser and not to this set would be handled and then reported as unhandled.
    /// </remarks>
    internal static readonly HashSet<string> Mapped = new(StringComparer.Ordinal) {
        Http, HttpLabel, HttpQuery, HttpHeader, HttpPayload, HttpError,
        Error, Required, Default, ClientOptional, Documentation, Deprecated, Title, Tags,
        EnumValue, JsonName, TimestampFormat, MediaType, Sparse,
        Length, Range, Pattern, UniqueItems,
        Readonly, Idempotent, Input, Output, Private, Internal, Mixin,
        Trait
    };

    /// <summary>
    /// Traits deliberately ignored, because the server behaves the same with or without them.
    /// </summary>
    /// <remarks>
    /// Three groups, and they are ignorable for different reasons. Documentation and metadata
    /// (<c>examples</c>, <c>since</c>, <c>externalDocumentation</c>) describe the model, not the
    /// wire. Client concerns (<c>paginated</c>, <c>idempotencyToken</c>, <c>requestCompression</c>,
    /// <c>endpoint</c>, <c>hostLabel</c>) describe how a caller behaves, and a server that ignores
    /// them still answers correctly. Deployment and protocol concerns (<c>cors</c>, the
    /// <c>xml*</c> family, the auth traits) belong to something other than the description - CORS is
    /// configured on the host, XML applies to a protocol this does not serve, and authentication is
    /// Hardened's own story rather than the IDL's.
    /// </remarks>
    internal static readonly HashSet<string> Ignorable = new(StringComparer.Ordinal) {
        "smithy.api#examples", "smithy.api#recommended", "smithy.api#unstable",
        "smithy.api#since", "smithy.api#externalDocumentation", "smithy.api#suppress",
        "smithy.api#traitValidators", "smithy.api#idRef", "smithy.api#references",
        "smithy.api#paginated", "smithy.api#idempotencyToken", "smithy.api#httpChecksumRequired",
        "smithy.api#requestCompression", "smithy.api#requiresLength", "smithy.api#endpoint",
        "smithy.api#hostLabel", "smithy.api#cors", "smithy.api#sensitive",
        "smithy.api#noReplace", "smithy.api#nestedProperties", "smithy.api#notProperty",
        "smithy.api#property", "smithy.api#resourceIdentifier", "smithy.api#unitType",
        "smithy.api#addedDefault", "smithy.api#box", "smithy.api#retryable",
        "smithy.api#auth", "smithy.api#optionalAuth", "smithy.api#authDefinition",
        "smithy.api#protocolDefinition", "smithy.api#httpApiKeyAuth", "smithy.api#httpBasicAuth",
        "smithy.api#httpBearerAuth", "smithy.api#httpDigestAuth",
        "smithy.api#xmlAttribute", "smithy.api#xmlFlattened", "smithy.api#xmlName",
        "smithy.api#xmlNamespace",
        "smithy.api#eventHeader", "smithy.api#eventPayload"
    };

    /// <summary>
    /// Traits that change the contract and have no IR equivalent, so the code is weaker than the
    /// model. Warned rather than dropped.
    /// </summary>
    internal static readonly HashSet<string> Degrades = new(StringComparer.Ordinal) {
        HttpResponseCode, HttpPrefixHeaders, HttpQueryParams
    };

    /// <summary>
    /// The protocols this front end can serve.
    /// </summary>
    /// <remarks>
    /// A model with no protocol trait at all is well formed, validates, and is what a hand-written
    /// model that only uses <c>@http</c> looks like - so absence is accepted rather than treated as
    /// an omission. What is refused is a protocol that is named and is not one of these, because
    /// generating REST routes for an <c>awsJson1_1</c> model is not weaker, it is wrong: every
    /// operation there is POST / dispatched on an X-Amz-Target header.
    /// </remarks>
    internal static readonly HashSet<string> SupportedProtocols = new(StringComparer.Ordinal) {
        "aws.protocols#restJson1"
    };

    /// <summary>
    /// Protocols that dispatch on a header rather than routing, and the header each uses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are RPC protocols wearing HTTP as an envelope: every operation is <c>POST /</c> and
    /// which one it is comes from <c>X-Amz-Target</c>, carrying <c>Service.Operation</c>. Both
    /// versions differ only in their content type and in how errors are shaped, which is why one
    /// entry each is enough here.
    /// </para>
    /// <para>
    /// They are supported rather than refused because they cost less than restJson1, not more:
    /// dispatch is an exact-match switch instead of a route tree, and binding is one body
    /// deserialize instead of a path, query, header and body split. The specification says HTTP
    /// binding traits "MUST be ignored if they are present", so there is nothing to reconcile.
    /// </para>
    /// </remarks>
    internal static readonly Dictionary<string, string> DispatchProtocols =
        new(StringComparer.Ordinal) {
            ["aws.protocols#awsJson1_0"] = "X-Amz-Target",
            ["aws.protocols#awsJson1_1"] = "X-Amz-Target"
        };

    /// <summary>The content type each dispatch protocol sends and answers with.</summary>
    internal static readonly Dictionary<string, string> DispatchContentTypes =
        new(StringComparer.Ordinal) {
            ["aws.protocols#awsJson1_0"] = "application/x-amz-json-1.0",
            ["aws.protocols#awsJson1_1"] = "application/x-amz-json-1.1"
        };

    /// <summary>Protocol traits that are recognised and refused, with the reason.</summary>
    internal static readonly Dictionary<string, string> RefusedProtocols =
        new(StringComparer.Ordinal) {
            ["aws.protocols#restXml"] =
                "XML bodies need a serializer this generator does not emit",
            ["aws.protocols#awsQuery"] =
                "form-encoded requests and XML responses are not served by this generator",
            ["aws.protocols#ec2Query"] =
                "form-encoded requests and XML responses are not served by this generator"
        };

    /// <summary>Whether a trait needs no diagnostic - it was read, or it is deliberately ignored.</summary>
    internal static bool IsAccountedFor(string traitId) =>
        Mapped.Contains(traitId) || Ignorable.Contains(traitId) || Degrades.Contains(traitId);
}
