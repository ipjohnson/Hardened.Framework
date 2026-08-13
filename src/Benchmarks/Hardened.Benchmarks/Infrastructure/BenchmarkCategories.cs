namespace Hardened.Benchmarks.Infrastructure;

/// <summary>
/// Category names used by <c>[BenchmarkCategory]</c> and by the command line filtering in
/// <c>Program</c>.
///
/// <see cref="Micro"/>, <see cref="Pipeline"/> and <see cref="Startup"/> run by default.
/// <see cref="AspNet"/> is opt-in via <c>--aspnet</c>, because comparing against another
/// framework is a question you ask deliberately rather than every time you want to know whether
/// Hardened regressed.
/// </summary>
public static class BenchmarkCategories {
    /// <summary>Individual Hardened components measured in isolation.</summary>
    public const string Micro = "micro";

    /// <summary>Whole request through Hardened, from context to serialized bytes.</summary>
    public const string Pipeline = "pipeline";

    /// <summary>Application construction and first-request cost.</summary>
    public const string Startup = "startup";

    /// <summary>Comparisons against ASP.NET Core MVC and minimal APIs. Opt-in.</summary>
    public const string AspNet = "aspnet";

    public static readonly string[] DefaultCategories = [Micro, Pipeline, Startup];
}
