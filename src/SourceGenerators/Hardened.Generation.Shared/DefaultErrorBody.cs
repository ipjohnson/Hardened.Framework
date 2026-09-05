using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Hardened.Generation;
using Hardened.Generation.Models;

namespace Hardened.Generation;

/// <summary>
/// The body a handler's null return writes, decided once for both sides that need to agree.
/// </summary>
/// <remarks>
/// <para>
/// A handler returning null answers 404, and used to answer it with no body at all - so an operation
/// whose document promised a <c>Problem</c> for its 404 sent a response the contract does not
/// describe and a generated client cannot read.
/// </para>
/// <para>
/// This lives in the shared assembly because two separate processes have to reach the same
/// conclusion from it. The build task emits the instance into the models file; the Roslyn generator,
/// which never sees that file, emits the handler info that names it. If one decides a schema can be
/// filled and the other does not, the generated code references a field that was never written and
/// the project does not compile. They call this rather than agreeing by inspection.
/// </para>
/// <para>
/// What goes in it is deliberately narrow, because the alternative is inventing domain values. Three
/// sources, in order:
/// </para>
/// <list type="number">
/// <item>The schema's own <c>default</c>, which is the document saying what it wants.</item>
/// <item>
/// RFC 7807's <c>type</c>, <c>title</c> and <c>status</c>, where the schema declares them with 7807's
/// types. Those members have specified meanings - <c>status</c> <em>is</em> the HTTP status code -
/// so filling them is conformance rather than a guess. Matched on shape, not on the schema's name,
/// so an unrelated schema whose <c>status</c> means something else is not mistaken for one.
/// </item>
/// <item>
/// Smithy's <c>message</c>, on a shape the model declared with <c>@error</c>. The specification
/// gives that member one meaning, so the reason phrase goes in it for the same reason it goes in
/// 7807's <c>title</c>. Keyed on the trait rather than the member's name: an ordinary payload
/// with a <c>message</c> is not an error.
/// </item>
/// <item>Nothing. An optional member with neither is left at its C# default.</item>
/// </list>
/// <para>
/// A <em>required</em> member with neither cannot be filled without making something up, so no
/// instance is offered and the operation writes no body. The remedy is to declare a <c>default</c>
/// on the member, or to throw the generated exception type, which carries a body the handler wrote.
/// </para>
/// </remarks>
internal static class DefaultErrorBody {

    /// <summary>The holder every generated instance is a field on.</summary>
    public const string HolderTypeName = "DefaultErrorBodies";

    /// <summary>The field's simple name for a schema and status.</summary>
    /// <remarks>
    /// <c>ShippedResponses.StatusName</c> rather than a table of its own. This file used to keep
    /// one, derived from <see cref="ReasonPhrase"/> - which is a wire phrase and answers a different
    /// question - and the two agreed for 404 and disagreed for 429. A field naming a status and a
    /// type naming the same status now read the same.
    /// </remarks>
    public static string FieldName(string schemaName, int statusCode) =>
        ShippedResponses.StatusName(statusCode) + NamingHelper.ToPascalCase(schemaName);

    /// <summary>
    /// The schema a null return would answer this operation with, or null where there is none.
    /// </summary>
    /// <remarks>
    /// Only GET and PUT, because those are the two verbs whose null result is a 404 - see
    /// <c>NullValueResponseHandler</c>. Restricted to a 404 the operation itself declares with a
    /// body: a document that never mentions one is not given one here.
    /// </remarks>
    public static string? SchemaFor(OperationModel operation) {
        if (operation.HttpMethod != "GET" && operation.HttpMethod != "PUT") {
            return null;
        }

        foreach (var error in operation.ErrorResponses) {
            if (error.StatusCode == 404 && error.Ref != null) {
                return TypeMapper.GetRefName(error.Ref);
            }
        }

        return null;
    }

    /// <summary>The status a null return answers this operation with. Always 404 today.</summary>
    public const int NullResponseStatus = 404;

    /// <summary>
    /// Every constructor argument for the instance, in declaration order, or null when a required
    /// member cannot be filled without inventing a value.
    /// </summary>
    public static IReadOnlyList<string>? Arguments(
        IReadOnlyList<SchemaModel> schemas, string schemaName, int statusCode) =>
        Arguments(schemas, schemaName, statusCode, BodySource.ForStatus(statusCode));

    /// <summary>
    /// The same arguments with <c>type</c>, <c>title</c>, <c>status</c> and <c>detail</c> read off a
    /// shipped record - the body <c>return new NotFound("todo", "...")</c> converts into - rather
    /// than derived from the status alone.
    /// </summary>
    /// <param name="record">The expression that holds the record, in the generated code.</param>
    public static IReadOnlyList<string>? ArgumentsFromRecord(
        IReadOnlyList<SchemaModel> schemas, string schemaName, int statusCode, string record) =>
        Arguments(schemas, schemaName, statusCode, BodySource.ForRecord(statusCode, record));

    private static IReadOnlyList<string>? Arguments(
        IReadOnlyList<SchemaModel> schemas, string schemaName, int statusCode, BodySource source) {
        var schema = Find(schemas, schemaName);

        if (schema == null || schema.Kind != SchemaKind.Object) {
            return null;
        }

        var isProblem = IsProblemDetails(schema);
        var arguments = new List<string>();

        foreach (var property in SchemaShape.Constructor(schema)) {
            var csType = TypeMapper.MapPropertyToCSharpType(property);
            var value = Value(property, csType, source, isProblem, schema.IsErrorShape);

            if (value != null) {
                arguments.Add(value);
                continue;
            }

            // Optional in C#, so the type's own default is a legitimate answer rather than an
            // invention - the member simply is not present in the response.
            if (property.HasDefault) {
                arguments.Add("default");
                continue;
            }

            // Required, and nothing says what it should hold. Rather than put a null into a member
            // the contract declares as required, offer no instance at all.
            return null;
        }

        return arguments;
    }

    public static SchemaModel? Find(IReadOnlyList<SchemaModel> schemas, string schemaName) {
        var wanted = NamingHelper.ToPascalCase(schemaName);

        return schemas.FirstOrDefault(
            candidate => NamingHelper.ToPascalCase(candidate.Name) == wanted);
    }

    private static string? Value(
        PropertyModel property, string csType, BodySource source, bool isProblem, bool isErrorShape) {
        // The document's own default first. It is the one source here that is not an inference.
        var declared = DefaultLiteral.Format(property.Default, csType);

        if (declared != null) {
            return declared;
        }

        // Smithy's message member, which the specification defines as the error message of an
        // @error structure. Filling it is the same act as filling 7807's title: the description
        // said what the member means, so writing the reason phrase into it is conformance rather
        // than a guess. Keyed on the shape carrying @error rather than on the member's name,
        // because { message: string } is an ordinary shape and an ordinary payload's message is
        // not this.
        if (isErrorShape && property.Name == "message" && csType == "string") {
            return source.Message;
        }

        if (!isProblem) {
            return null;
        }

        switch (property.Name) {
            case "type" when csType == "string":
                return source.Type;
            case "title" when csType == "string":
                return source.Title;
            case "status" when csType == "int" || csType == "long":
                return source.Status;
            case "detail" when csType == "string" || csType == "string?":
                return source.Detail;
            default:
                return null;
        }
    }

    /// <summary>
    /// Where the members a problem body can be filled from come from: the status alone, for the
    /// body a null return writes, or a shipped record, for the body a returned record converts
    /// into.
    /// </summary>
    /// <remarks>
    /// The status source writes <c>type</c> as the framework's problem type for the status - the
    /// URI a code-first handler's record sends - and <c>about:blank</c> only where the framework
    /// declares none, so a null return and a returned <c>NotFound</c> answer with one <c>type</c>.
    /// The record source reads the four members off the record, and fills a Smithy <c>message</c>
    /// with the detail, or the title where no detail was given, which is the same act as the reason
    /// phrase going into it.
    /// </remarks>
    private readonly struct BodySource {
        private BodySource(string type, string title, string status, string? detail, string message) {
            Type = type;
            Title = title;
            Status = status;
            Detail = detail;
            Message = message;
        }

        public string Type { get; }

        public string Title { get; }

        public string Status { get; }

        public string? Detail { get; }

        public string Message { get; }

        public static BodySource ForStatus(int statusCode) {
            var phrase = "\"" + ReasonPhrase(statusCode) + "\"";
            var problemType = ShippedResponses.ProblemType(statusCode);

            return new BodySource(
                problemType == null
                    ? "\"about:blank\""
                    : "global::" + ShippedResponses.Namespace + ".ProblemTypes." + problemType,
                phrase,
                statusCode.ToString(CultureInfo.InvariantCulture),
                detail: null,
                phrase);
        }

        public static BodySource ForRecord(int statusCode, string record) {
            // MethodNotAllowed and NotAcceptable carry a status and nothing else a problem body
            // reads, so their bodies are the status's rather than the record's.
            if (!ShippedResponses.HasProblemMembers(statusCode)) {
                var fromStatus = ForStatus(statusCode);

                return new BodySource(
                    fromStatus.Type, fromStatus.Title, record + ".Status", detail: null, fromStatus.Message);
            }

            return new BodySource(
                record + ".Type", record + ".Title", record + ".Status", record + ".Detail",
                "(" + record + ".Detail ?? " + record + ".Title)");
        }
    }

    /// <summary>
    /// Whether the schema is RFC 7807's problem shape, by its members rather than its name.
    /// </summary>
    /// <remarks>
    /// <c>title</c> and <c>status</c> together, with 7807's types. A schema carrying a <c>status</c>
    /// that means something else - an order's, a job's - does not also carry a string <c>title</c>
    /// beside it often enough for this to be worth loosening, and the cost of a false positive is a
    /// status code written into a member that never meant one.
    /// </remarks>
    public static bool IsProblemDetails(SchemaModel schema) {
        var hasTitle = false;
        var hasStatus = false;

        foreach (var property in schema.Properties) {
            var csType = TypeMapper.MapPropertyToCSharpType(property);

            if (property.Name == "title" && csType == "string") {
                hasTitle = true;
            }
            else if (property.Name == "status" && (csType == "int" || csType == "long")) {
                hasStatus = true;
            }
        }

        return hasTitle && hasStatus;
    }

    /// <summary>
    /// The reason phrase, which is public information about the status and nothing else.
    /// </summary>
    /// <remarks>
    /// A value written into a generated body's <c>title</c>, which is the whole of what it is for.
    /// It used to compose the field's name as well, and the two questions had drifted - the phrase
    /// for 429 is "Too Many Requests" and the framework's record for it is <c>RateLimited</c>.
    /// Naming is <c>ShippedResponses.StatusName</c>'s.
    /// </remarks>
    public static string ReasonPhrase(int statusCode) {
        switch (statusCode) {
            case 400: return "Bad Request";
            case 401: return "Unauthorized";
            case 403: return "Forbidden";
            case 404: return "Not Found";
            case 405: return "Method Not Allowed";
            case 406: return "Not Acceptable";
            case 409: return "Conflict";
            case 410: return "Gone";
            case 415: return "Unsupported Media Type";
            case 422: return "Unprocessable Content";
            case 429: return "Too Many Requests";
            case 500: return "Internal Server Error";
            case 503: return "Service Unavailable";
            default: return "Error";
        }
    }
}
