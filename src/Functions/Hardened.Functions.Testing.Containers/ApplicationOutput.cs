namespace Hardened.Functions.Testing.Containers;

/// <summary>
/// Where a fixture application's build output is, so a container can mount it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The build output rather than a publish.</b> An executable project's <c>bin</c> already holds
/// everything a framework-dependent host needs - the assembly, its dependencies, <c>deps.json</c>
/// and <c>runtimeconfig.json</c> - and it exists whenever the test project has been built, because
/// the test project references the application. A publish step would have to run inside the test,
/// and <c>dotnet test --no-build</c> in CI would then be the first time anything noticed it missing.
/// </para>
/// <para>
/// The application is found beside the test project, by the layout every integration fixture in
/// this repository uses: <c>Fixture/Application/bin/Configuration/net8.0</c> next to
/// <c>Fixture/Application.Tests/bin/Configuration/net8.0</c>, same configuration on both sides.
/// </para>
/// <para>
/// The application must build with <c>UseAppHost=false</c>. An apphost is a native executable for
/// the machine that built it, and one built on macOS is not something a Linux container can start;
/// the assembly is what the host runs, and it is what the mount is for.
/// </para>
/// </remarks>
public static class ApplicationOutput {
    /// <summary>
    /// The output directory of the sibling application <paramref name="projectName"/>, or an
    /// exception naming what was expected and where.
    /// </summary>
    public static string Of(string projectName) {
        var testOutput = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

        // bin/<Configuration>/net8.0 is three segments; the project directory is above them.
        var framework = Path.GetFileName(testOutput);
        var configuration = Path.GetFileName(Path.GetDirectoryName(testOutput)!);
        var fixtureDirectory = Path.GetFullPath(Path.Combine(testOutput, "..", "..", "..", ".."));

        var output = Path.Combine(fixtureDirectory, projectName, "bin", configuration, framework);
        var assembly = Path.Combine(output, projectName + ".dll");

        if (!File.Exists(assembly)) {
            throw new DirectoryNotFoundException(
                $"No build output for {projectName} at '{output}'. The test project has to reference " +
                "the application so it is built first, and both have to be built in the same " +
                $"configuration ({configuration}).");
        }

        return output;
    }
}
