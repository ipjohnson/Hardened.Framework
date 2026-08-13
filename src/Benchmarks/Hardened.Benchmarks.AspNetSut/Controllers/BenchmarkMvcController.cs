using Hardened.Benchmarks.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Hardened.Benchmarks.AspNetSut.Controllers;

/// <summary>
/// The MVC implementation of the five benchmark scenarios.
///
/// Routes, binding sources and response shapes match the Hardened controller exactly, so the
/// only difference between the two measurements is the framework doing the work. MVC carries
/// real per-request machinery Hardened does not have an equivalent for — action descriptors, the
/// filter pipeline, model binding, result execution — which is the point of including it: it is
/// what most teams are actually running.
/// </summary>
[ApiController]
[Route("bench")]
public class BenchmarkMvcController : ControllerBase {

    [HttpGet("item")]
    public ItemResponse Item() {
        return new ItemResponse {
            Id = 1,
            Name = "benchmark",
            Active = true,
            Score = 99.5
        };
    }

    [HttpGet("item/{id:int}")]
    public ItemResponse ItemById(int id) {
        return new ItemResponse {
            Id = id,
            Name = "benchmark",
            Active = true,
            Score = 99.5
        };
    }

    [HttpGet("query")]
    public ItemResponse Query([FromQuery] int page, [FromQuery] int size) {
        return new ItemResponse {
            Id = page * size,
            Name = "benchmark",
            Active = true,
            Score = 99.5
        };
    }

    [HttpPost("sum")]
    public SumResponse Sum([FromServices] ISumService sumService, [FromBody] SumRequest request) {
        var values = request.Values ?? new List<int>();

        return new SumResponse {
            Id = request.Id,
            Label = request.Label,
            Sum = sumService.Sum(values),
            Count = values.Count
        };
    }

    [HttpGet("binding/{id}")]
    public BindingResponse Binding(
        string id,
        [FromQuery] string filter,
        [FromHeader(Name = "X-Tenant")] string tenant) {
        return new BindingResponse {
            Id = id,
            Filter = filter,
            Tenant = tenant
        };
    }
}
