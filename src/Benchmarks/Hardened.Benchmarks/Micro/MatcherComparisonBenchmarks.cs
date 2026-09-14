using BenchmarkDotNet.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Benchmarks.Contracts;
using Hardened.Benchmarks.Infrastructure;
using Hardened.Requests.Abstract.Execution;
using Microsoft.AspNetCore.Http;

namespace Hardened.Benchmarks.Micro;

/// <summary>
/// Hardened's compiled radix tree against ASP.NET Core's DFA, matching only.
///
/// <para>
/// The two do the same job by opposite means. Hardened's Web generator emits a trie over the
/// path's characters as one C# method per node, decided at compile time. ASP.NET builds a state
/// machine over the path's segments at run time, walks it with a jump table per state, and then
/// works through a candidate set. Neither number here includes binding, handler invocation or
/// serialization.
/// </para>
/// <para>
/// Both sides are driven from <see cref="RouteCatalog"/>. Hardened's half of it is the generated
/// controller in the matching RouteScale SUT, ASP.NET's is read at run time, and
/// <see cref="Setup"/> asserts that both match every template before anything is timed. A
/// matcher that silently routes nothing is very fast.
/// </para>
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory(BenchmarkCategories.AspNet)]
public class MatcherComparisonBenchmarks
{
    private HardenedMatcherHarness _hardened = null!;
    private AspNetMatcherHarness _aspNet = null!;
    private IExecutionContext _hardenedContext = null!;
    private DefaultHttpContext _aspNetContext = null!;

    public IEnumerable<RouteScale> ScaleValues => RouteScale.All;

    public IEnumerable<MatchScenario> ScenarioValues => MatchScenario.All;

    [ParamsSource(nameof(ScaleValues))]
    public RouteScale Scale { get; set; } = null!;

    [ParamsSource(nameof(ScenarioValues))]
    public MatchScenario Scenario { get; set; } = null!;

    [GlobalSetup]
    public void Setup()
    {
        _hardened = new HardenedMatcherHarness(Scale.Application());
        _aspNet = new AspNetMatcherHarness(Scale.Routes);

        _hardenedContext = _hardened.CreateContext(Scenario.Method, Scenario.Path);
        _aspNetContext = AspNetMatcherHarness.CreateContext(Scenario.Method, Scenario.Path);

        VerifyCatalogIsRoutable();
        VerifyScenarioAgrees();
    }

    /// <summary>
    /// Every template in the catalog resolves on both sides.
    ///
    /// This is what ties the generated controller to the list ASP.NET is given. The two are
    /// emitted by one script, but only one of them is compiled in, so nothing else would notice
    /// a generated file that had drifted from the catalog beside it.
    /// </summary>
    private void VerifyCatalogIsRoutable()
    {
        foreach (var (method, template) in Scale.Routes)
        {
            var path = MatchScenario.FillTokens(template);

            if (_hardened.Match(_hardened.CreateContext(method, path))?.Handler == null)
            {
                throw new InvalidOperationException(
                    $"Hardened did not route {method} {path} (from template {template}) at scale "
                        + $"{Scale.Name}. The generated controller and the catalog disagree; "
                        + "re-run scripts/generate-route-scale-sut.py."
                );
            }

            var aspNetContext = AspNetMatcherHarness.CreateContext(method, path);

            _aspNet.Match(aspNetContext).GetAwaiter().GetResult();

            if (aspNetContext.GetEndpoint() == null)
            {
                throw new InvalidOperationException(
                    $"ASP.NET did not route {method} {path} (from template {template}) at scale "
                        + $"{Scale.Name}."
                );
            }
        }
    }

    /// <summary>
    /// The scenario resolves the same way on both sides, or misses on both.
    ///
    /// A scenario that matches on one side and not the other would be timing a match against a
    /// rejection, which is the cheaper of the two and would read as a win.
    /// </summary>
    private void VerifyScenarioAgrees()
    {
        var hardenedMatched = _hardened.Match(_hardenedContext)?.Handler != null;

        _aspNet.Match(_aspNetContext).GetAwaiter().GetResult();

        var aspNetMatched = _aspNetContext.GetEndpoint() != null;

        if (hardenedMatched != aspNetMatched || hardenedMatched != Scenario.ShouldMatch)
        {
            throw new InvalidOperationException(
                $"{Scenario.Name} at scale {Scale.Name}: expected match={Scenario.ShouldMatch}, "
                    + $"Hardened={hardenedMatched}, ASP.NET={aspNetMatched}."
            );
        }
    }

    [Benchmark(Baseline = true)]
    public object? Hardened() => _hardened.Match(_hardenedContext);

    /// <summary>
    /// The returned task is consumed rather than awaited. <c>DfaMatcher.MatchAsync</c> runs to
    /// completion synchronously for every scenario here, which <see cref="Setup"/> establishes by
    /// reading the endpoint back, so awaiting it would add the cost of reading a result that is
    /// already there to one side of the comparison only.
    /// </summary>
    [Benchmark]
    public object AspNetDfa() => _aspNet.Match(_aspNetContext);

    [GlobalCleanup]
    public void Cleanup()
    {
        _hardened.Dispose();
        _aspNet.Dispose();
    }
}

/// <summary>One arm of the scale axis: a route count and the application compiled for it.</summary>
public sealed class RouteScale
{
    private readonly Func<IDependencyModule> _application;

    private RouteScale(
        string name,
        IReadOnlyList<(string Method, string Template)> routes,
        Func<IDependencyModule> application
    )
    {
        Name = name;
        Routes = routes;
        _application = application;
    }

    public string Name { get; }

    public IReadOnlyList<(string Method, string Template)> Routes { get; }

    public IDependencyModule Application() => _application();

    public static readonly RouteScale[] All =
    [
        new(
            "14",
            RouteCatalog.Small,
            static () => new Benchmarks.RouteScale.Small.Sut.RouteScaleSmallApplication()
        ),
        new(
            "105",
            RouteCatalog.Medium,
            static () => new Benchmarks.RouteScale.Medium.Sut.RouteScaleMediumApplication()
        ),
        new(
            "504",
            RouteCatalog.Large,
            static () => new Benchmarks.RouteScale.Large.Sut.RouteScaleLargeApplication()
        ),
    ];

    public override string ToString() => Name;
}

/// <summary>One path to match, chosen to isolate a single thing the two matchers do differently.</summary>
public sealed class MatchScenario
{
    /// <summary>
    /// A 36 character identifier. Entity ids in a real API are this shape rather than two digits,
    /// and the cost of a token is not constant in its length on either side.
    /// </summary>
    private const string Guid = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";

    private MatchScenario(string name, string method, string path, bool shouldMatch = true)
    {
        Name = name;
        Method = method;
        Path = path;
        ShouldMatch = shouldMatch;
    }

    public string Name { get; }

    public string Method { get; }

    public string Path { get; }

    public bool ShouldMatch { get; }

    /// <summary>A concrete path for a template, for the setup check that every route resolves.</summary>
    public static string FillTokens(string template)
    {
        var path = template;

        while (path.IndexOf('{') is var open and >= 0)
        {
            var close = path.IndexOf('}', open);

            path = path[..open] + "42" + path[(close + 1)..];
        }

        return path;
    }

    public static readonly MatchScenario[] All =
    [
        // A literal path that ends at a leaf. No token, so both sides can answer from a cached
        // result and neither allocates.
        new("literal-shallow", "GET", "/api/v1/users"),
        // Four literal segments behind a shared prefix. This is where comparing a literal one
        // character at a time is furthest from comparing a whole segment.
        new("literal-deep", "GET", "/api/v1/reports/users/summary/daily"),
        // A token that ends the path. Hardened rejects a deeper path with one vectorised
        // IndexOf; ASP.NET already knows the segment count.
        new("token-terminal", "GET", "/api/v1/users/42"),
        // A token with a literal after it: the case Hardened resolves by scanning for the
        // boundary and ASP.NET gets from its tokenizer.
        new("token-mid-short", "GET", "/api/v1/users/42/events"),
        // The same shape with a realistic identifier, which is what separates the cost of the
        // scan from the cost of the match.
        new("token-mid-guid", "GET", $"/api/v1/users/{Guid}/events"),
        // Two tokens and a literal between them.
        new("token-two", "GET", "/api/v1/users/42/events/981"),
        // A path no route declares, below a prefix several of them share. Rejection has to walk
        // as far as a match before it can fail.
        new("miss", "GET", "/api/v1/users/42/unknown", shouldMatch: false),
    ];

    public override string ToString() => Name;
}
