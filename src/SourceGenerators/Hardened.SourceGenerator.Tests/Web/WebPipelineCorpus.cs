namespace Hardened.SourceGenerator.Tests.Web;

/// <summary>
/// Applications covering what the attribute-routed pipeline emits.
/// </summary>
/// <remarks>
/// Chosen by what reaches a different part of the pipeline rather than by feature name: a body
/// binds through a different path from a query parameter, a constraint reaches the routing table
/// where a status reaches the handler, and an entry-point attribute changes the table itself. Two
/// scenarios that would emit the same shapes are one scenario.
/// </remarks>
public static class WebPipelineCorpus {
    public static readonly string[] Scenarios = [
        "literal-get",
        "path-and-query",
        "body-post",
        "constrained-token",
        "verb-set",
        "declared-status",
        "injected-service"
    ];

    public static string Source(string scenario) =>
        Application(scenario switch {
            "literal-get" => """
                public class HomeController {
                    [Get("/hello")]
                    public string Hello() => "hello";
                }
                """,

            "path-and-query" => """
                public class SearchController {
                    [Get("/search/{category}")]
                    public Task<string> Search(string category, [FromQueryString] int? limit) =>
                        Task.FromResult(category + limit);
                }
                """,

            "body-post" => """
                public record Order(string Sku, int Quantity);

                public class OrderController {
                    [Post("/orders")]
                    public Task<Order> Place(Order body) => Task.FromResult(body);
                }
                """,

            "constrained-token" => """
                public class ItemController {
                    [Get("/items/{id:int}")]
                    public Task<string> Get(int id) => Task.FromResult(id.ToString());

                    [Get("/slugs/{name:slug}")]
                    public Task<string> BySlug(string name) => Task.FromResult(name);
                }
                """,

            "verb-set" => """
                public class TicketController {
                    [Get("/tickets/{id}")]
                    public Task<string> Get(string id) => Task.FromResult(id);

                    [Put("/tickets/{id}")]
                    public Task<string> Replace(string id) => Task.FromResult(id);

                    [Delete("/tickets/{id}")]
                    public Task Remove(string id) => Task.CompletedTask;
                }
                """,

            "declared-status" => """
                public class WidgetController {
                    [Post("/widgets", SuccessStatus = 201)]
                    public Task<string> Create() => Task.FromResult("made");

                    [Get("/widgets/{id}")]
                    public Task<string?> Find(string id) => Task.FromResult<string?>(null);
                }
                """,

            "injected-service" => """
                public interface IClock { string Now(); }

                public class ClockController {
                    [Get("/now")]
                    public Task<string> Now([FromServices] IClock clock) => Task.FromResult(clock.Now());
                }
                """,

            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown scenario.")
        });

    /// <summary>
    /// The module declaration every scenario hangs off. Partial because the table is emitted into it.
    /// </summary>
    private static string Application(string body) => $$"""
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Hardened.Requests.Abstract.Attributes;
        using Hardened.Shared.Runtime.Attributes;
        using Hardened.Web.Runtime.Attributes;

        namespace TestApp;

        [HardenedModule]
        public partial class Application { }

        {{body}}
        """;
}
