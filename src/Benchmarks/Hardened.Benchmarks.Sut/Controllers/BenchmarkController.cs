using Hardened.Benchmarks.Contracts;
using Hardened.Web.Runtime.Attributes;

namespace Hardened.Benchmarks.Sut.Controllers;

/// <summary>
/// The five benchmark scenarios, implemented in Hardened.
///
/// Every route here has a byte-for-byte equivalent in the ASP.NET SUT — same path, same binding
/// sources, same response shape, same trivial amount of handler work. The handler bodies are
/// kept nearly free of computation on purpose: the intent is to measure what the framework does
/// around the handler, so any real work inside it would be noise added equally to both sides
/// while shrinking the relative difference being measured.
/// </summary>
[BasePath("/bench")]
public class BenchmarkController {

    /// <summary>Dispatch and serialize, with nothing to bind. The floor for a request.</summary>
    [Get("/item")]
    public ItemResponse Item() {
        return new ItemResponse {
            Id = 1,
            Name = "benchmark",
            Active = true,
            Score = 99.5
        };
    }

    /// <summary>Adds a single typed path token, so route matching has to capture and convert.</summary>
    [Get("/item/{id}")]
    public ItemResponse ItemById(int id) {
        return new ItemResponse {
            Id = id,
            Name = "benchmark",
            Active = true,
            Score = 99.5
        };
    }

    /// <summary>Two typed query string values, the other common binding source.</summary>
    [Get("/query")]
    public ItemResponse Query([FromQueryString] int page, [FromQueryString] int size) {
        return new ItemResponse {
            Id = page * size,
            Name = "benchmark",
            Active = true,
            Score = 99.5
        };
    }

    /// <summary>
    /// The full read path: deserialize a JSON body, resolve a scoped service, serialize a
    /// different type back out.
    /// </summary>
    [Post("/sum")]
    public SumResponse Sum(ISumService sumService, SumRequest request) {
        var values = request.Values ?? new List<int>();

        return new SumResponse {
            Id = request.Id,
            Label = request.Label,
            Sum = sumService.Sum(values),
            Count = values.Count
        };
    }

    /// <summary>
    /// Path token, query string and header in one handler — the case where per-source binding
    /// overhead is most visible.
    /// </summary>
    [Get("/binding/{id}")]
    public BindingResponse Binding(
        string id,
        [FromQueryString] string filter,
        [FromHeader("X-Tenant")] string tenant) {
        return new BindingResponse {
            Id = id,
            Filter = filter,
            Tenant = tenant
        };
    }
}
