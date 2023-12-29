using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Shared;

internal class SourceGeneratorWrapper {
    public static Action<SourceProductionContext, T> Wrap<T>(Action<SourceProductionContext, T> writeSourceFile) {
        return (context, value) => {
            try {
                writeSourceFile(context, value);
            }
            catch (Exception exp) {
                var descriptor = new DiagnosticDescriptor(
                    id: "HardenedException",
                    title: "Unexpected Error",
                    messageFormat: "Error for object: {0}",
                    category: "Design",
                    defaultSeverity: DiagnosticSeverity.Warning,
                    isEnabledByDefault: true);

                context.ReportDiagnostic(Diagnostic.Create(descriptor, Location.None, GetExceptionMessage(exp)));
            }
        };
    }

    private static string GetExceptionMessage(Exception exp) {
        return $"Report the exception:  {exp.Message} {exp.TargetSite.DeclaringType?.FullName} {exp.TargetSite}";
    }
}