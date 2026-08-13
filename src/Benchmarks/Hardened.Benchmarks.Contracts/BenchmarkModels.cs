using System.Text.Json.Serialization;

namespace Hardened.Benchmarks.Contracts;

/// <summary>
/// Response for the no-parameter and path-token scenarios. Deliberately small: four primitives
/// plus one string, which is representative of a real API response without letting serialization
/// cost dominate the dispatch cost the pipeline benchmarks are trying to isolate.
/// </summary>
public class ItemResponse {
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public bool Active { get; set; }

    public double Score { get; set; }
}

/// <summary>
/// Request body for the POST scenarios. The list is what makes deserialization non-trivial —
/// a body of only scalars would understate the cost of the read path.
/// </summary>
public class SumRequest {
    public int Id { get; set; }

    public string? Label { get; set; }

    public List<int>? Values { get; set; }
}

public class SumResponse {
    public int Id { get; set; }

    public string? Label { get; set; }

    public int Sum { get; set; }

    public int Count { get; set; }
}

/// <summary>
/// Response for the multi-source binding scenario, where the point is to observe binding cost
/// rather than serialization cost.
/// </summary>
public class BindingResponse {
    public string Id { get; set; } = "";

    public string Filter { get; set; } = "";

    public string Tenant { get; set; } = "";
}

/// <summary>
/// Source-generated metadata for the models above.
///
/// This exists so the ASP.NET comparison can be run in both of its serialization modes.
/// ASP.NET minimal APIs and MVC default to reflection-based System.Text.Json, while Hardened's
/// <c>AotJsonSerializer</c> uses source-generated <c>JsonTypeInfo</c>. Comparing those two
/// directly would measure System.Text.Json's two modes rather than the two pipelines, so the
/// ASP.NET side is benchmarked with and without this context and the difference is reported as
/// its own axis.
/// </summary>
[JsonSerializable(typeof(ItemResponse))]
[JsonSerializable(typeof(SumRequest))]
[JsonSerializable(typeof(SumResponse))]
[JsonSerializable(typeof(BindingResponse))]
public partial class BenchmarkJsonContext : JsonSerializerContext { }
