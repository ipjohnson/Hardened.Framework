using System.Collections.Generic;
using Hardened.Generation.Models;

namespace Hardened.Generation;

/// <summary>
/// The conversion a declared error offers from the bare shipped record for its status: what
/// <c>return new NotFound("todo", "...")</c> becomes in a response set.
/// </summary>
/// <remarks>
/// <para>
/// A declared error reaches a response set as a case carrying the contract's own body -
/// <c>NotFound&lt;Problem&gt;</c> for an anonymous OpenAPI error, a generated
/// <c>TodoNotFoundError</c> for a named Smithy one - and a handler that only wants to say
/// "not found, and here is why" had to build that body by hand: type, title and status filled in
/// from what the case already knew. This is the rule for when the build can do that instead. The
/// record knows the facts of its status, the schema says which members hold them, and
/// <see cref="DefaultErrorBody"/> already maps one onto the other for the body a null return
/// writes. What a handler adds is the detail.
/// </para>
/// <para>
/// One rule, asked by the emitter that writes the operator on the set and by the one that writes
/// the method the operator calls, so the two cannot disagree about which cases convert.
/// </para>
/// </remarks>
internal static class ProblemConversion {

    /// <summary>The holder's name, which is the file's plus a suffix, reserved by the allocator.</summary>
    public static string HolderName(string specFileName) =>
        NamingHelper.ToPascalCase(specFileName) + "Problems";

    /// <summary>The expression the generated method reads the record through.</summary>
    public const string Record = "value";

    /// <summary>The body's constructor arguments, read off <paramref name="record"/>.</summary>
    /// <remarks>Never null for a plan <see cref="For"/> returned, which checked the same call.</remarks>
    public static IReadOnlyList<string> Arguments(
        Plan plan, IReadOnlyList<SchemaModel> schemas, string record) =>
        DefaultErrorBody.ArgumentsFromRecord(schemas, plan.SchemaName, plan.StatusCode, record)!;

    internal readonly struct Plan {
        public Plan(
            int statusCode, string bareRecord, string schemaName, string caseTypeName,
            bool caseIsShipped) {
            StatusCode = statusCode;
            BareRecord = bareRecord;
            SchemaName = schemaName;
            CaseTypeName = caseTypeName;
            CaseIsShipped = caseIsShipped;
        }

        public int StatusCode { get; }

        /// <summary>The shipped record the operator converts from - <c>NotFound</c>.</summary>
        public string BareRecord { get; }

        /// <summary>The contract's body schema, as named in the document.</summary>
        public string SchemaName { get; }

        /// <summary>
        /// The case the set holds: the shipped generic form's name, or the generated case type's.
        /// </summary>
        public string CaseTypeName { get; }

        /// <summary>Whether the case is a shipped generic record over the body, or a generated type.</summary>
        public bool CaseIsShipped { get; }

        /// <summary>The generated method's name, unique per record and body.</summary>
        public string MethodName => BareRecord + NamingHelper.ToPascalCase(SchemaName);

        /// <summary>A stable key for one plan across the operations that share it.</summary>
        public string Key => StatusCode + ":" + SchemaName;
    }

    /// <summary>
    /// The conversion this error offers, or null where there is nothing to build it from.
    /// </summary>
    /// <remarks>
    /// Null for an error with no body, whose case already is the bare record; for one declaring
    /// a header, whose case carries a value no record has; for a body that is not problem shaped,
    /// or a Smithy error shape with a required member nothing can fill; and for a status the
    /// framework ships no record for.
    /// </remarks>
    public static Plan? For(ErrorResponseModel error, IReadOnlyList<SchemaModel> schemas) {
        if (error.Ref == null || error.Headers.Count > 0) {
            return null;
        }

        var bare = ShippedResponses.BareForm(error.StatusCode);

        if (bare == null || error.StatusCode == 304) {
            return null;
        }

        var schemaName = TypeMapper.GetRefName(error.Ref);
        var schema = DefaultErrorBody.Find(schemas, schemaName);

        if (schema == null) {
            return null;
        }

        var binding = ShippedResponses.For(error);

        if (binding != null) {
            // A shipped generic record over the contract's body, which converts when the body is
            // RFC 7807 shaped. A marker form - Status<Http.Locked, Problem> - has no bare record.
            if (binding.Value.Marker != null || !DefaultErrorBody.IsProblemDetails(schema)) {
                return null;
            }

            return Fillable(schemas, schemaName, error.StatusCode)
                ? new Plan(error.StatusCode, bare, schemaName, binding.Value.TypeName, caseIsShipped: true)
                : null;
        }

        // A generated case type carrying a named error shape: Smithy's @error structure, whose
        // message the record's detail fills. Anything else it requires has no source.
        if (error.TypeName == null || !schema.IsErrorShape) {
            return null;
        }

        return Fillable(schemas, schemaName, error.StatusCode)
            ? new Plan(error.StatusCode, bare, schemaName, error.TypeName, caseIsShipped: false)
            : null;
    }

    /// <summary>Whether every required member of the body has a source on the record.</summary>
    private static bool Fillable(IReadOnlyList<SchemaModel> schemas, string schemaName, int statusCode) =>
        DefaultErrorBody.ArgumentsFromRecord(schemas, schemaName, statusCode, Record) != null;
}
