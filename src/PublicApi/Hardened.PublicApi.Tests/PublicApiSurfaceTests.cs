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
        "Hardened.Commands",
        "Hardened.Requests.Abstract",
        "Hardened.Requests.Runtime",
        "Hardened.Requests.Serializers.Newtonsoft",
        "Hardened.Requests.Testing",
        "Hardened.Shared.Runtime",
        "Hardened.Shared.Testing",
        "Hardened.SourceGeneration.Testing",
        "Hardened.Templates.Abstract",
        "Hardened.Templates.Runtime",
        "Hardened.Web.AspNetCore.Runtime",
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
                "System.Runtime.CompilerServices.RuntimeCompatibilityAttribute"
            ]
        }));

        var approvedPath = ApprovedPath(assemblyName);

        if (Approving) {
            Directory.CreateDirectory(Path.GetDirectoryName(approvedPath)!);
            File.WriteAllText(approvedPath, actual);

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

    private static string ApprovedPath(string assemblyName) =>
        Path.Combine(SourceDirectory(), "Approved", assemblyName + ".approved.txt");

    private static void WriteReceived(string assemblyName, string actual) {
        var path = Path.Combine(SourceDirectory(), "Approved", assemblyName + ".received.txt");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, actual);
    }

    private static void DeleteStaleReceived(string assemblyName) {
        var path = Path.Combine(SourceDirectory(), "Approved", assemblyName + ".received.txt");

        if (File.Exists(path)) {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The directory this file lives in, so approved files sit beside the test in source control
    /// rather than in the build output where they cannot be committed.
    /// </summary>
    private static string SourceDirectory([CallerFilePath] string path = "") =>
        Path.GetDirectoryName(path)!;

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
