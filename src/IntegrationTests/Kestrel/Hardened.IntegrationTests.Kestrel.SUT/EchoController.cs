using Hardened.Web.Runtime.Attributes;

namespace Hardened.IntegrationTests.Kestrel.SUT;

public class EchoModel {
    public int Id { get; set; }

    public string? Label { get; set; }

    public List<int>? Values { get; set; }
}

public class EchoResult {
    public string Method { get; set; } = "";

    public string Id { get; set; } = "";

    public string Filter { get; set; } = "";

    public string Tenant { get; set; } = "";

    public string Label { get; set; } = "";

    public int Sum { get; set; }
}

/// <summary>
/// Covers each binding source over a real socket, so that anything the feature-based request
/// implementation gets wrong about Kestrel's representations shows up as a failed assertion
/// rather than as a subtly wrong value.
/// </summary>
[BasePath("/echo")]
public class EchoController {

    [Get("/plain")]
    public EchoResult Plain() => new() { Method = "GET" };

    [Get("/path/{id}")]
    public EchoResult FromPath(string id) => new() { Method = "GET", Id = id };

    [Get("/query")]
    public EchoResult FromQuery([FromQueryString] string filter) =>
        new() { Method = "GET", Filter = filter };

    [Get("/header")]
    public EchoResult FromHeader([FromHeader("X-Tenant")] string tenant) =>
        new() { Method = "GET", Tenant = tenant };

    [Get("/mixed/{id}")]
    public EchoResult Mixed(
        string id,
        [FromQueryString] string filter,
        [FromHeader("X-Tenant")] string tenant) =>
        new() { Method = "GET", Id = id, Filter = filter, Tenant = tenant };

    [Post("/body")]
    public EchoResult FromBody(EchoModel model) => new() {
        Method = "POST",
        Id = model.Id.ToString(),
        Label = model.Label ?? "",
        Sum = model.Values?.Sum() ?? 0
    };

    /// <summary>Exercises the top-level exception guard in <c>HardenedHttpApplication</c>.</summary>
    [Get("/throw")]
    public EchoResult Throws() => throw new InvalidOperationException("deliberate");
}
