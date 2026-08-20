using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Hardened.Idl.Emitters;
using Hardened.Idl.Models;

namespace Hardened.Idl;

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
    public static string FieldName(string schemaName, int statusCode) =>
        StatusName(statusCode) + NamingHelper.ToPascalCase(schemaName);

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
        IReadOnlyList<SchemaModel> schemas, string schemaName, int statusCode) {
        var schema = Find(schemas, schemaName);

        if (schema == null || schema.Kind != SchemaKind.Object) {
            return null;
        }

        var isProblem = IsProblemDetails(schema);
        var arguments = new List<string>();

        foreach (var property in SchemaShape.Constructor(schema)) {
            var csType = TypeMapper.MapPropertyToCSharpType(property);
            var value = Value(property, csType, statusCode, isProblem);

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
        PropertyModel property, string csType, int statusCode, bool isProblem) {
        // The document's own default first. It is the one source here that is not an inference.
        var declared = DefaultLiteral.Format(property.Default, csType);

        if (declared != null) {
            return declared;
        }

        if (!isProblem) {
            return null;
        }

        switch (property.Name) {
            case "type" when csType == "string":
                return "\"about:blank\"";
            case "title" when csType == "string":
                return "\"" + ReasonPhrase(statusCode) + "\"";
            case "status" when csType == "int" || csType == "long":
                return statusCode.ToString(CultureInfo.InvariantCulture);
            default:
                return null;
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
    private static bool IsProblemDetails(SchemaModel schema) {
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

    private static string StatusName(int statusCode) {
        var phrase = ReasonPhrase(statusCode);

        return phrase == "Error"
            ? "Status" + statusCode.ToString(CultureInfo.InvariantCulture)
            : phrase.Replace(" ", "");
    }
}
