using CSharpAuthor;

namespace Hardened.Amz.Function.Lambda.SourceGenerator;

/// <summary>
/// The two lines that put a handler on <c>Amazon.Lambda.RuntimeSupport</c>'s bootstrap. Without
/// their terminators, which the statement writer adds.
/// </summary>
/// <remarks>
/// Written as text rather than composed, because the builder has thirty-eight <c>Create</c>
/// overloads and the one the host needs - the raw stream handler, the shape for custom serializers
/// and Native AOT - is selected by casting the method group to its delegate type. Fully qualified
/// throughout, since the file is written in global type-output mode and nothing here is imported.
/// The web generator carries the same class; a generator cannot share an assembly with another.
/// </remarks>
internal static class BootstrapEmitter {
    private const string RawStreamHandler =
        "global::System.Func<global::System.IO.Stream, global::Amazon.Lambda.Core.ILambdaContext, " +
        "global::System.Threading.Tasks.Task<global::System.IO.Stream>>";

    /// <param name="handler">The method group, as written at the call site.</param>
    public static IOutputComponent Build(string handler) =>
        CodeOutputComponent.Get(
            "using var bootstrap = global::Amazon.Lambda.RuntimeSupport.LambdaBootstrapBuilder.Create(" +
            $"({RawStreamHandler}){handler}).Build()");

    public static IOutputComponent Run() =>
        CodeOutputComponent.Get("await bootstrap.RunAsync(global::System.Threading.CancellationToken.None)");
}
