using Hardened.Benchmarks.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Hardened.Benchmarks.AspNetSut;

/// <summary>
/// The minimal API implementation of the five benchmark scenarios.
///
/// This is the closest analogue to Hardened of anything in ASP.NET: both compile a delegate per
/// route ahead of time rather than resolving an action through descriptors at request time, so
/// it is the fairer of the two ASP.NET comparisons. MVC is included alongside it as the
/// more commonly deployed shape rather than the more comparable one.
/// </summary>
public static class MinimalApiEndpoints {

    public static void MapBenchmarkEndpoints(this IEndpointRouteBuilder endpoints) {
        endpoints.MapGet("/bench/item", () => new ItemResponse {
            Id = 1,
            Name = "benchmark",
            Active = true,
            Score = 99.5
        });

        endpoints.MapGet("/bench/item/{id:int}", (int id) => new ItemResponse {
            Id = id,
            Name = "benchmark",
            Active = true,
            Score = 99.5
        });

        endpoints.MapGet("/bench/query", (int page, int size) => new ItemResponse {
            Id = page * size,
            Name = "benchmark",
            Active = true,
            Score = 99.5
        });

        endpoints.MapPost("/bench/sum", (ISumService sumService, SumRequest request) => {
            var values = request.Values ?? new List<int>();

            return new SumResponse {
                Id = request.Id,
                Label = request.Label,
                Sum = sumService.Sum(values),
                Count = values.Count
            };
        });

        endpoints.MapGet("/bench/binding/{id}",
            (string id, string filter, [Microsoft.AspNetCore.Mvc.FromHeader(Name = "X-Tenant")] string tenant) =>
                new BindingResponse {
                    Id = id,
                    Filter = filter,
                    Tenant = tenant
                });
    }
}
