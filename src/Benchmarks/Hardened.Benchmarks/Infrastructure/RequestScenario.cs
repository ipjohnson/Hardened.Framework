using System.Text;
using System.Text.Json;
using Hardened.Benchmarks.Contracts;

namespace Hardened.Benchmarks.Infrastructure;

/// <summary>
/// One request, described independently of any framework.
///
/// All four pipelines are driven from these same values, so a difference in a result is a
/// difference in the framework rather than in what it was asked to do. The body is pre-serialized
/// to bytes here rather than per-invocation: serializing the request is the caller's cost in
/// reality, and leaving it inside the measured region would add identical work to every pipeline
/// while diluting the differences between them.
/// </summary>
public sealed class RequestScenario {
    public required string Name { get; init; }

    public required string Method { get; init; }

    /// <summary>Path only. The query string is kept separate because the two frameworks parse it
    /// at different points and each should be given it in the form it expects.</summary>
    public required string Path { get; init; }

    public string? QueryString { get; init; }

    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>();

    public byte[]? Body { get; init; }

    public string? ContentType { get; init; }

    /// <summary>
    /// The status every pipeline is expected to return. Verification asserts it; benchmarks
    /// ignore it.
    /// </summary>
    public int ExpectedStatus { get; init; } = 200;

    public string FullPath => QueryString is null ? Path : $"{Path}?{QueryString}";

    public override string ToString() => Name;
}

public static class Scenarios {
    private static readonly byte[] SumBody = Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new SumRequest {
            Id = 7,
            Label = "benchmark",
            Values = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]
        }));

    /// <summary>Dispatch plus serialization, nothing bound.</summary>
    public static readonly RequestScenario Item = new() {
        Name = "GET item",
        Method = "GET",
        Path = "/bench/item"
    };

    /// <summary>One typed path token.</summary>
    public static readonly RequestScenario ItemById = new() {
        Name = "GET item/{id}",
        Method = "GET",
        Path = "/bench/item/42"
    };

    /// <summary>Two typed query string values.</summary>
    public static readonly RequestScenario Query = new() {
        Name = "GET query",
        Method = "GET",
        Path = "/bench/query",
        QueryString = "page=2&size=10"
    };

    /// <summary>JSON body in, different type out, one scoped service resolved.</summary>
    public static readonly RequestScenario Sum = new() {
        Name = "POST sum",
        Method = "POST",
        Path = "/bench/sum",
        Body = SumBody,
        ContentType = "application/json"
    };

    /// <summary>Path token, query string and header in a single handler.</summary>
    public static readonly RequestScenario Binding = new() {
        Name = "GET binding/{id}",
        Method = "GET",
        Path = "/bench/binding/abc",
        QueryString = "filter=active",
        Headers = new Dictionary<string, string> { ["X-Tenant"] = "tenant-1" }
    };

    /// <summary>
    /// A route no pipeline has, which every pipeline must answer with a 404.
    ///
    /// Verification-only — it measures the miss path rather than the work the benchmarks are
    /// about. It exists because this is the shape of bug that has bitten twice: both
    /// <c>FeatureExecutionResponse</c> and <c>AspNetExecutionResponse</c> read <c>Status</c> back
    /// off the server's response object, which starts at 200, so <c>ResourceNotFoundHandler</c> —
    /// which only fills in a 404 when it finds the status unset — silently never fired.
    /// </summary>
    public static readonly RequestScenario NotFound = new() {
        Name = "GET not-found",
        Method = "GET",
        Path = "/bench/no-such-route",
        ExpectedStatus = 404
    };

    /// <summary>The scenarios benchmarks run. All are expected to succeed.</summary>
    public static readonly RequestScenario[] All = [Item, ItemById, Query, Sum, Binding];

    /// <summary>The scenarios verification runs — the benchmark set plus the miss path.</summary>
    public static readonly RequestScenario[] Verification = [.. All, NotFound];
}
