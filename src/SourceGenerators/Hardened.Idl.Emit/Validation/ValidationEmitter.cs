using System.Collections.Generic;
using System.Linq;
using CSharpAuthor;
using Hardened.Idl.Models;

namespace Hardened.Idl.Validation;

/// <summary>
/// The validation a spec declares: a parameter interface per constrained operation, and the
/// <c>[GeneratedRegex]</c> members its patterns point at.
/// </summary>
/// <remarks>
/// <para>
/// No validators are emitted here, and no ValidationModules IR is built. The task writes attributes;
/// <c>Hardened.Validation.SourceGenerator</c> reads them out of the compilation and emits the
/// validators - the same scan that picks up <c>[Required]</c> on a class a developer wrote. One
/// front-end rather than two that meet in the middle.
/// </para>
/// <para>
/// The <c>[GeneratedRegex]</c> class is the one thing only this half can produce. A source generator
/// cannot emit it - its output is not in the compilation the regex generator reads - so patterns
/// would fall back to a constructed <c>Regex</c>, at 448 KB on an AOT publish against 33 KB.
/// </para>
/// </remarks>
internal static class ValidationEmitter {

    public static IReadOnlyList<OperationParameters.Model> Emit(
        NamespaceDefinition validation,
        ServiceSpecModel model,
        string modelsNamespace,
        PatternRegistry patterns) {
        var operations = new List<OperationParameters.Model>();

        foreach (var service in model.Services) {
            foreach (var operation in service.Operations) {
                var parameters = OperationParameters.Build(operation, model, modelsNamespace, patterns);

                if (parameters == null) {
                    continue;
                }

                operations.Add(parameters);
                EmitInterface(validation, parameters);
            }
        }

        return operations;
    }

    private static void EmitInterface(NamespaceDefinition validation, OperationParameters.Model model) {
        var definition = validation.AddInterface(model.InterfaceName);

        definition.Modifiers |= ComponentModifier.Public | ComponentModifier.Partial;

        foreach (var member in model.Members) {
            var property = definition.AddProperty(member.Type, member.Name);

            property.Set = null;

            foreach (var attribute in member.Attributes) {
                Apply(property, attribute);
            }
        }
    }

    /// <summary>
    /// Adds one constraint attribute to a member. No target: an interface property is a property,
    /// unlike a positional record parameter.
    /// </summary>
    public static AttributeDefinition Apply(BaseOutputComponent member, ConstraintAttributes.Model attribute) =>
        member.AddAttribute(
            attribute.Type,
            attribute.Arguments.Select(argument =>
                (object)new CodeOutputComponent(argument) { Indented = false }).ToArray());

    /// <summary>
    /// The <c>[GeneratedRegex]</c> members, one per distinct pattern in the spec.
    /// </summary>
    /// <remarks>
    /// Written as a raw component rather than through <c>AddClass</c>. These are partial method
    /// declarations with no body - the regex generator supplies the implementation - and
    /// <c>MethodDefinition</c> always writes a body, so <c>ComponentModifier.Partial</c> on a method
    /// produces <c>static Regex P_x() { }</c> and SYSLIB1043. The same escape hatch
    /// SpecRoutingTableGenerator uses for statements CSharpAuthor has no construct for.
    /// </remarks>
    public static void EmitPatterns(NamespaceDefinition validation, PatternRegistry patterns) {
        if (patterns.IsEmpty) {
            return;
        }

        var builder = new System.Text.StringBuilder();

        builder.AppendLine($"internal static partial class {patterns.ClassName}");
        builder.AppendLine("{");

        foreach (var pair in patterns.Members) {
            builder.AppendLine(
                $"    [global::System.Text.RegularExpressions.GeneratedRegex({Quote(pair.Key)})]");
            builder.AppendLine(
                $"    public static partial global::System.Text.RegularExpressions.Regex {pair.Value}();");
        }

        builder.Append("}");

        validation.AddComponent(new CodeOutputComponent(builder.ToString()) { Indented = true });
    }

    private static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
