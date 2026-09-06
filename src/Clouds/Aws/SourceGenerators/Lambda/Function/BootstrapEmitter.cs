using CSharpAuthor;

namespace Hardened.Amz.Function.Lambda.SourceGenerator;

/// <summary>
/// The lines that put a handler on <c>Amazon.Lambda.RuntimeSupport</c>'s bootstrap. Without their
/// terminators, which the statement writer adds.
/// </summary>
/// <remarks>
/// <para>
/// Written as text rather than composed, because the builder has thirty-eight <c>Create</c>
/// overloads and the one the host needs - the raw stream handler, the shape for custom serializers
/// and Native AOT - is selected by casting the method group to its delegate type. Fully qualified
/// throughout, since the file is written in global type-output mode and nothing here is imported.
/// </para>
/// <para>
/// The bootstrap is pointed at whatever <see cref="StartEmulator"/> produced. On Lambda that is
/// null, which the builder treats as unset and reads <c>AWS_LAMBDA_RUNTIME_API</c> as before.
/// Locally it is the AWS Lambda Test Tool the session started, so F5 runs this <c>Main</c> rather
/// than a second project.
/// </para>
/// </remarks>
internal static class BootstrapEmitter {
    private const string RawStreamHandler =
        "global::System.Func<global::System.IO.Stream, global::Amazon.Lambda.Core.ILambdaContext, " +
        "global::System.Threading.Tasks.Task<global::System.IO.Stream>>";

    private const string Emulator = "global::Hardened.Amz.Shared.Lambda.Runtime.Development.LambdaEmulator";

    /// <param name="apiGateway">
    /// Whether the tool's API Gateway emulator fronts the function locally: true for a web
    /// application, false for a function.
    /// </param>
    public static IOutputComponent StartEmulator(bool apiGateway) =>
        CodeOutputComponent.Get(
            $"using var emulator = await {Emulator}.StartIfLocal(app.GetType(), {apiGateway.ToString().ToLowerInvariant()})");

    /// <param name="handler">The method group, as written at the call site.</param>
    public static IOutputComponent Build(string handler) =>
        CodeOutputComponent.Get(
            "using var bootstrap = global::Amazon.Lambda.RuntimeSupport.LambdaBootstrapBuilder.Create(" +
            $"({RawStreamHandler}){handler})" +
            ".ConfigureOptions(options => options.RuntimeApiEndpoint = emulator.RuntimeApiEndpoint).Build()");

    public static IOutputComponent Run() =>
        CodeOutputComponent.Get("await bootstrap.RunAsync(global::System.Threading.CancellationToken.None)");
}
