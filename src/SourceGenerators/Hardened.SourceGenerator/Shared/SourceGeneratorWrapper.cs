using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Shared;

/// <summary>
/// Catches anything thrown while a generator writes source, so one failing item does not take the
/// compiler down with it.
/// </summary>
internal class SourceGeneratorWrapper {

    /// <summary>
    /// Reported when a generator throws while emitting. Named here so tests and the generator test
    /// harness can recognise it rather than matching on a message.
    /// </summary>
    public const string DiagnosticId = "HardenedException";

    public static Action<SourceProductionContext, T> Wrap<T>(Action<SourceProductionContext, T> writeSourceFile) {
        return (context, value) => {
            try {
                writeSourceFile(context, value);
            }
            catch (Exception exp) {
                // Error, not warning. Reaching here means the generator emitted nothing for this
                // item, so whatever depended on it is missing and the build cannot succeed in any
                // useful sense - a consumer without TreatWarningsAsErrors would otherwise carry on
                // and be told only about the downstream damage, with the cause filed as a warning
                // they may never see.
                //
                // It also closes a hole in the test suites. GeneratorResult.AssertNoErrors checks
                // for Error-severity diagnostics, so while this was a warning a generator could
                // crash, produce no code at all, and every OutputCompiles test over it still
                // passed. Three suites had grown their own local check for this diagnostic to work
                // around it.
                var descriptor = new DiagnosticDescriptor(
                    id: DiagnosticId,
                    title: "Generator failed",
                    messageFormat: "The generator threw and produced no source: {0}",
                    category: "Hardened.Generation",
                    defaultSeverity: DiagnosticSeverity.Error,
                    isEnabledByDefault: true);

                context.ReportDiagnostic(Diagnostic.Create(descriptor, Location.None, GetExceptionMessage(exp)));
            }
        };
    }

    private static string GetExceptionMessage(Exception exp) {
        // TargetSite is null for an exception that never unwound a managed frame, and this used to
        // dereference it - so the handler for a generator crash could itself throw, replacing a
        // reported diagnostic with an unhandled one.
        var site = exp.TargetSite;

        var where = site == null
            ? "(no target site)"
            : $"{site.DeclaringType?.FullName}.{site}";

        return $"{exp.GetType().Name}: {exp.Message} at {where}";
    }
}
