using System.Reflection;
using System.Runtime.CompilerServices;
using PublicApiGenerator;
using Xunit;

namespace Hardened.PublicApi.Tests;

/// <summary>
/// The public surface of every shipped assembly, checked in and compared on every run.
///
/// <para>
/// This exists because of <c>[Delete]</c>. <c>DeleteAttribute</c> and <c>PatchAttribute</c> shipped
/// from the first commit in 2022 as <c>internal class DeleteAttribute { }</c> — not public, and not
/// derived from <see cref="Attribute"/>, so no consuming project could apply them. The generator
/// recognised the verbs, the runtime routed them, and the README and package description both
/// advertised <c>[Delete]</c>. It took three years and a documentation pass to notice, because
/// nothing asserted what the assembly actually exposes.
/// </para>
///
/// <para>
/// An approved file is not a rubber stamp. A diff here means the shipped contract changed: review it
/// as an API change, then re-approve deliberately.
/// </para>
/// </summary>
public class PublicApiSurfaceTests {

    /// <summary>
    /// Set <c>APPROVE_PUBLIC_API=1</c> to rewrite the approved files from the current surface.
    /// Never set in CI — the workflow would approve its own regressions.
    /// </summary>
    private static bool Approving =>
        Environment.GetEnvironmentVariable("APPROVE_PUBLIC_API") == "1";

    /// <summary>Every shipped net8.0 assembly, by name.</summary>
    private static readonly string[] Shipped = [
        "Hardened.Requests.Abstract",
        "Hardened.Requests.Runtime",
        "Hardened.Requests.Serializers.Newtonsoft",
        "Hardened.Requests.Testing",
        "Hardened.Shared.Runtime",
        "Hardened.Shared.Testing",
        "Hardened.SourceGeneration.Testing",
        "Hardened.Templates.RazorBlade",
        "Hardened.Web.AspNetCore.Runtime",
        "Hardened.Web.Kestrel.Runtime",
        "Hardened.Web.Runtime",
        "Hardened.Web.Testing"
    ];

    public static TheoryData<string> ShippedAssemblies() => new(Shipped);

    [Theory]
    [MemberData(nameof(ShippedAssemblies))]
    public void PublicSurfaceMatchesTheApprovedFile(string assemblyName) {
        var assembly = Assembly.Load(assemblyName);

        var actual = Normalise(assembly.GeneratePublicApi(new ApiGeneratorOptions {
            ExcludeAttributes = [
                // Build-stamped, so they differ per machine and per configuration.
                "System.Runtime.Versioning.TargetFrameworkAttribute",
                "System.Reflection.AssemblyMetadataAttribute",
                "System.Diagnostics.DebuggableAttribute",
                "System.Runtime.CompilerServices.CompilationRelaxationsAttribute",
                "System.Runtime.CompilerServices.RuntimeCompatibilityAttribute",

                // xunit.v3 records the source file and line of every [Fact] and [Theory], which
                // Hardened.Requests.Testing ships on its conformance suite. Those arguments are
                // absolute paths, so they differ between a developer machine and a deterministic
                // CI build - and the line numbers churn whenever anyone edits the file, which
                // would fail this test on edits that change no public surface at all. The method
                // signatures themselves are still compared.
                "Xunit.FactAttribute",
                "Xunit.TheoryAttribute"
            ]
        }));

        var approvedPath = ApprovedPath(assemblyName);

        if (Approving) {
            var directory = SourceDirectory();

            if (directory == null) {
                // Not a skip: CI fails on any skipped test, and this is a misused flag rather than
                // an environment that cannot run the check.
                Assert.Fail(
                    "APPROVE_PUBLIC_API is set but the source directory is not reachable from " +
                    "this build, so there is nowhere to write the approved file. Approve from a " +
                    "developer machine, not from a deterministic build.");
            }

            var sourcePath = Path.Combine(directory, "Approved", assemblyName + ".approved.txt");

            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            File.WriteAllText(sourcePath, actual);

            return;
        }

        if (!File.Exists(approvedPath)) {
            WriteReceived(assemblyName, actual);

            Assert.Fail(
                $"{assemblyName} has no approved public API file." + Environment.NewLine +
                $"  expected: {approvedPath}" + Environment.NewLine +
                "  Review the .received.txt written beside it, then re-run with APPROVE_PUBLIC_API=1.");
        }

        var approved = Normalise(File.ReadAllText(approvedPath));

        if (approved == actual) {
            DeleteStaleReceived(assemblyName);

            return;
        }

        WriteReceived(assemblyName, actual);

        Assert.Fail(
            $"The public API of {assemblyName} changed." + Environment.NewLine + Environment.NewLine +
            Describe(approved, actual) + Environment.NewLine +
            "  If the change is intended, re-run with APPROVE_PUBLIC_API=1 and commit the approved file." +
            Environment.NewLine +
            "  If it is not, something shipped is no longer reachable by consumers.");
    }

    /// <summary>
    /// A package added to the repository without an entry in <see cref="ShippedAssemblies"/> would
    /// ship with no approved surface and nobody would notice. This catches that.
    /// </summary>
    [Fact]
    public void EveryReferencedShippedAssemblyIsCovered() {
        var covered = Shipped.ToHashSet(StringComparer.Ordinal);

        var referenced = typeof(PublicApiSurfaceTests).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name)
            .Where(name => name != null && name.StartsWith("Hardened.", StringComparison.Ordinal))
            .Select(name => name!)
            .ToHashSet(StringComparer.Ordinal);

        var uncovered = referenced.Except(covered).OrderBy(name => name, StringComparer.Ordinal).ToList();

        Assert.True(uncovered.Count == 0,
            "These Hardened assemblies are referenced but have no approved public API:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, uncovered.Select(name => "  " + name)) +
            Environment.NewLine +
            "Add each to ShippedAssemblies and approve its surface.");
    }

    /// <summary>
    /// Read from the build output, which the csproj copies the approved files into. Reading them
    /// through <see cref="SourceDirectory"/> instead is what broke CI: a deterministic build
    /// rewrites the compile-time path to a placeholder, so the lookup pointed at <c>/_/src/...</c>.
    /// </summary>
    private static string ApprovedPath(string assemblyName) =>
        Path.Combine(AppContext.BaseDirectory, "Approved", assemblyName + ".approved.txt");

    /// <summary>
    /// Written beside the approved file in source control, so a diff can be eyeballed and then
    /// approved. Only possible on a developer machine — see <see cref="SourceDirectory"/>. In CI
    /// there is nowhere useful to put it and the failure message carries the diff instead.
    /// </summary>
    private static void WriteReceived(string assemblyName, string actual) {
        var directory = SourceDirectory();

        if (directory == null) {
            return;
        }

        var path = Path.Combine(directory, "Approved", assemblyName + ".received.txt");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, actual);
    }

    private static void DeleteStaleReceived(string assemblyName) {
        var directory = SourceDirectory();

        if (directory == null) {
            return;
        }

        var path = Path.Combine(directory, "Approved", assemblyName + ".received.txt");

        if (File.Exists(path)) {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The directory this file lives in, so an approved file can be written back into source
    /// control rather than into the build output where it cannot be committed.
    ///
    /// <para>
    /// Null when the compile-time path does not exist on this machine, which is the case for any
    /// deterministic build: <c>ContinuousIntegrationBuild=true</c> rewrites it to a placeholder
    /// like <c>/_/src/...</c>. Callers must handle that rather than assuming a writable path —
    /// assuming one failed every surface test in CI while passing locally.
    /// </para>
    /// </summary>
    private static string? SourceDirectory([CallerFilePath] string path = "") {
        var directory = Path.GetDirectoryName(path);

        return directory != null && Directory.Exists(directory) ? directory : null;
    }

    private static string Normalise(string api) =>
        api.Replace("\r\n", "\n").TrimEnd() + "\n";

    /// <summary>
    /// The added and removed lines, rather than two full API dumps. A surface file runs to hundreds
    /// of lines and the interesting part is usually one of them.
    /// </summary>
    private static string Describe(string approved, string actual) {
        var approvedLines = approved.Split('\n');
        var actualLines = actual.Split('\n');

        var removed = approvedLines.Except(actualLines, StringComparer.Ordinal).ToList();
        var added = actualLines.Except(approvedLines, StringComparer.Ordinal).ToList();

        var message = new System.Text.StringBuilder();

        foreach (var line in removed.Where(line => line.Trim().Length > 0).Take(40)) {
            message.AppendLine("  - " + line.Trim());
        }

        foreach (var line in added.Where(line => line.Trim().Length > 0).Take(40)) {
            message.AppendLine("  + " + line.Trim());
        }

        if (removed.Count + added.Count > 80) {
            message.AppendLine($"  … {removed.Count + added.Count - 80} more changed lines");
        }

        return message.ToString();
    }
}
