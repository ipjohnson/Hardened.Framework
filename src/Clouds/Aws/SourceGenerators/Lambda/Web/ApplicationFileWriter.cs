using CSharpAuthor;
using Hardened.SourceGenerator.Shared;

namespace Hardened.Amz.Web.Lambda.SourceGenerator;

/// <summary>
/// Assembles the generated application file. The class itself is written by
/// <see cref="LambdaWebApplicationFileWriter"/>.
///
/// <para>
/// A private <c>CreateApplicationClass</c> and the three methods it called used to sit below,
/// unreachable — a stale near-copy of the live writer that emitted the handler as
/// <c>FunctionHandlerAsync</c> where the live path emits <c>Invoke</c>. Two contradictory answers
/// to what the Lambda entry point is called, one of them wrong and neither compiled into anything.
/// Removed 2026-08-15.
/// </para>
/// </summary>
public static class ApplicationFileWriter {
    public static string WriteFile(EntryPointSelector.Model entryPoint) {
        var applicationFile = new CSharpFileDefinition(entryPoint.EntryPointType.Namespace);
        var lambdaFileWriter = new LambdaWebApplicationFileWriter();

        lambdaFileWriter.CreateApplicationClass(entryPoint, applicationFile);

        var context = new OutputContext(
            new OutputContextOptions {
                TypeOutputMode = TypeOutputMode.Global
            });

        applicationFile.WriteOutput(context);

        return context.Output();
    }
}
