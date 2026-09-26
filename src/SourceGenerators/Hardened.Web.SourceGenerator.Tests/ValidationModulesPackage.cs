using Microsoft.CodeAnalysis;
using ValidationModules.SourceGenerator;

namespace Hardened.Web.SourceGenerator.Tests;

/// <summary>
/// What referencing the ValidationModules.SourceGenerator package gives a project: its generator,
/// and the build property its targets make visible to the compiler.
/// </summary>
/// <remarks>
/// The handler generators read the property to learn that validators are being emitted. A run
/// with the generator and without the property is not a shape a real build produces, so the two
/// are handed out together.
/// </remarks>
internal static class ValidationModulesPackage
{
    public static IIncrementalGenerator Generator() => new ValidationSourceGenerator();

    /// <summary>Present with no value, which is how an unset property reaches a generator.</summary>
    public static IReadOnlyDictionary<string, string> BuildProperties { get; } =
        new Dictionary<string, string> { ["ValidationModules_Registration"] = "" };
}
