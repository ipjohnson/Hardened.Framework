using System.Collections.Generic;
using System.Linq;
using CSharpAuthor;
using Hardened.Generation;
using Hardened.Generation.Models;

namespace Hardened.Idl.Emitters;

/// <summary>
/// Writes the methods a response set's conversion from a bare shipped record calls.
/// </summary>
/// <remarks>
/// <para>
/// One static holder per file, <c>{File}Problems</c>, with one method per record and body pair the
/// file's response sets convert: <c>NotFoundProblem(NotFound value)</c> builds the
/// <c>NotFound&lt;Problem&gt;</c> case with the contract's <c>Problem</c> filled from the record.
/// Two operations declaring one 404 over one schema share the method, which is why it is not on
/// the set.
/// </para>
/// <para>
/// A record's <c>Default</c> instance converts to a cached case rather than a new one, so
/// <c>return NotFound.Default;</c> allocates nothing in either contract style. Compared by
/// reference, because the point is the instance a handler chose to share; an equal record built by
/// hand is a new answer and gets a new body.
/// </para>
/// <para>
/// Which cases convert is <see cref="ProblemConversion"/>'s decision, taken once for this and for
/// the operator <see cref="UnionResponseEmitter"/> writes on the set - a member of the struct, or
/// of the body a <c>union</c> declaration is given for it - so the two cannot disagree.
/// </para>
/// </remarks>
internal static class ProblemConversionEmitter {

    public static ClassDefinition? Emit(
        IConstructContainer container, IReadOnlyList<SchemaModel> schemas,
        IReadOnlyList<ProblemConversion.Plan> plans, string modelsNamespace, string specFileName) {
        if (plans.Count == 0) {
            return null;
        }

        var holder = container.AddClass(ProblemConversion.HolderName(specFileName));

        holder.Modifiers |= ComponentModifier.Public | ComponentModifier.Static;
        holder.Comment = DocComment.Format(
            "The cases a response set builds from a bare shipped record - what " +
            "`return new NotFound(\"todo\", \"...\")` becomes - with the contract's body filled " +
            "from what the record knows about its status. One method per record and body.");

        // One method per record and body however many operations declare the pair, and ordered,
        // so the emitted file is byte-stable between builds whatever order the operations were
        // walked in.
        var written = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (var plan in plans.OrderBy(candidate => candidate.Key, System.StringComparer.Ordinal)) {
            if (written.Add(plan.Key)) {
                EmitMethod(holder, schemas, plan, modelsNamespace);
            }
        }

        return holder;
    }

    private static void EmitMethod(
        ClassDefinition holder, IReadOnlyList<SchemaModel> schemas, ProblemConversion.Plan plan,
        string modelsNamespace) {
        var record = "global::" + ShippedResponses.Namespace + "." + plan.BareRecord;
        var body = "global::" + modelsNamespace + "." + NamingHelper.ToPascalCase(plan.SchemaName);
        var caseType = CaseType(plan, modelsNamespace, body);
        var construction = Construction(plan, schemas, body, caseType, ProblemConversion.Record);

        if (ShippedResponses.HasDefaultInstance(plan.StatusCode)) {
            var cached = plan.MethodName + "Default";

            holder.AddComponent(new CodeOutputComponent(
                $"private static readonly {caseType} {cached} = " +
                Construction(plan, schemas, body, caseType, record + ".Default") + ";") {
                Indented = true
            });

            holder.AddComponent(new CodeOutputComponent(
                $"public static {caseType} {plan.MethodName}({record} {ProblemConversion.Record}) => " +
                $"ReferenceEquals({ProblemConversion.Record}, {record}.Default) ? {cached} : {construction};") {
                Indented = true
            });

            return;
        }

        holder.AddComponent(new CodeOutputComponent(
            $"public static {caseType} {plan.MethodName}({record} {ProblemConversion.Record}) => {construction};") {
            Indented = true
        });
    }

    /// <summary>The case the method returns: the shipped generic form over the body, or the generated type.</summary>
    private static string CaseType(ProblemConversion.Plan plan, string modelsNamespace, string body) =>
        plan.CaseIsShipped
            ? "global::" + ShippedResponses.Namespace + "." + plan.CaseTypeName + "<" + body + ">"
            : "global::" + modelsNamespace + "." + plan.CaseTypeName;

    /// <summary>The case built from <paramref name="record"/>: its body, and for four records the header beside it.</summary>
    private static string Construction(
        ProblemConversion.Plan plan, IReadOnlyList<SchemaModel> schemas, string body, string caseType,
        string record) {
        var arguments = string.Join(", ", ProblemConversion.Arguments(plan, schemas, record));
        var payload = "new " + body + "(" + arguments + ")";

        return plan.CaseIsShipped
            ? "new " + caseType + "(" + ShippedResponses.GenericArguments(plan.StatusCode, payload, record) + ")"
            : "new " + caseType + "(" + payload + ")";
    }
}
