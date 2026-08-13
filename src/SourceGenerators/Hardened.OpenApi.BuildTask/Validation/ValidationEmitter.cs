using System.Collections.Generic;
using System.Linq;
using System.Text;
using Hardened.OpenApi.SourceGenerator.Models;
using ValidationModules.SourceGenerator.Impl.Emitters;

namespace Hardened.OpenApi.BuildTask.Validation;

/// <summary>
/// The validation half of a spec's generated file: a parameter interface per constrained operation,
/// the validators, and the <c>[GeneratedRegex]</c> members those validators call.
/// </summary>
/// <remarks>
/// <para>
/// The validators come from ValidationModules' <c>ValidatorEmitter</c> - the same one its own
/// generator uses - so a constraint written in a spec and one written as an attribute produce the
/// same code. Only the front-end differs.
/// </para>
/// <para>
/// The interface is the seam between this task and the source generator. The task cannot name the
/// handler's <c>Parameters</c> class: it is nested inside a handler type whose name carries a
/// computed suffix. It can name an interface, put it in the model, and let the generator add it to
/// the class it does know how to name.
/// </para>
/// </remarks>
internal static class ValidationEmitter {

    /// <summary>The emitted validation, with the usings it needs kept separate.</summary>
    /// <remarks>
    /// Separate because a using directive has to precede every type in a file, and this text is
    /// appended after the models. ValidatorEmitter writes its own preamble - it is built to produce
    /// a whole file - so the caller merges them into the one block at the top.
    /// </remarks>
    internal sealed record Emitted(IReadOnlyList<string> Usings, string Body);

    public static Emitted Emit(OpenApiSpecModel model, string rootNamespace) {
        var validation = SpecValidationFrontEnd.Build(model, rootNamespace);

        if (validation.Operations.Count == 0) {
            return new Emitted(System.Array.Empty<string>(), "");
        }

        // Recorded so the generator is told what to implement and what to register, rather than
        // deriving either of them a second time and drifting.
        model.ValidatedOperations = validation.Operations
            .Select(operation => new ValidatedOperationModel {
                OperationId = operation.OperationId,
                InterfaceName = operation.InterfaceName,
                ValidatorName = operation.ValidatorName,
            })
            .ToList();

        var builder = new StringBuilder();
        var validationNamespace = rootNamespace + ".Validation";

        builder.AppendLine();
        builder.AppendLine($"namespace {validationNamespace}");
        builder.AppendLine("{");

        EmitPatterns(builder, validation.Patterns);
        EmitInterfaces(builder, validation);

        // The validators go in the same block. ValidatorEmitter writes a file-scoped namespace
        // because it is built to produce a whole file, and a file may hold only one of those - so
        // its declaration is dropped and the block around it does the same job.
        var validatorEmitter = new ValidatorEmitter();
        var usings = new List<string>();

        foreach (var validator in validation.Validators) {
            builder.AppendLine();
            builder.Append(Indent(StripPreamble(validatorEmitter.Emit(validator), usings)));
        }

        builder.AppendLine("}");

        return new Emitted(usings, builder.ToString());
    }

    /// <summary>
    /// The <c>[GeneratedRegex]</c> members, which is the thing a source generator cannot emit and
    /// this task exists to provide.
    /// </summary>
    private static void EmitPatterns(StringBuilder builder, PatternRegistry patterns) {
        if (patterns.IsEmpty) {
            return;
        }

        builder.AppendLine($"    internal static partial class {patterns.ClassName}");
        builder.AppendLine("    {");

        foreach (var pair in patterns.Members) {
            builder.AppendLine(
                $"        [global::System.Text.RegularExpressions.GeneratedRegex({Quote(pair.Key)})]");
            builder.AppendLine(
                $"        public static partial global::System.Text.RegularExpressions.Regex {pair.Value}();");
            builder.AppendLine();
        }

        builder.AppendLine("    }");
        builder.AppendLine();
    }

    private static void EmitInterfaces(StringBuilder builder, SpecValidationFrontEnd.Result validation) {
        foreach (var operation in validation.Operations) {
            builder.AppendLine($"    public partial interface {operation.InterfaceName}");
            builder.AppendLine("    {");

            foreach (var member in operation.Members) {
                builder.AppendLine($"        {member.TypeName} {member.Name} {{ get; }}");
            }

            builder.AppendLine("    }");
            builder.AppendLine();
        }
    }

    /// <summary>
    /// Drops the per-file preamble ValidatorEmitter writes, collecting its usings for the caller to
    /// hoist. It emits a whole file; this is one part of one.
    /// </summary>
    private static string StripPreamble(string emitted, List<string> usings) {
        var lines = emitted.Split('\n');
        var start = 0;

        while (start < lines.Length) {
            var line = lines[start].Trim();

            if (line.StartsWith("using ", System.StringComparison.Ordinal)) {
                if (!usings.Contains(line)) {
                    usings.Add(line);
                }
            } else if (line.StartsWith("namespace ", System.StringComparison.Ordinal)) {
                // Dropped: the block this is emitted into already declares it.
            } else if (line.Length != 0 &&
                       !line.StartsWith("// <auto-generated/>", System.StringComparison.Ordinal) &&
                       !line.StartsWith("#nullable", System.StringComparison.Ordinal)) {
                break;
            }

            start++;
        }

        return string.Join("\n", lines.Skip(start));
    }

    /// <summary>Shifts a validator into the namespace block it is written inside.</summary>
    private static string Indent(string emitted) =>
        string.Join("\n", emitted.Split('\n').Select(line => line.Length == 0 ? line : "    " + line));

    private static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
