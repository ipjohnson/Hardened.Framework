using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Execution;
using Hardened.Web.Runtime.Handlers;
using Hardened.Web.Runtime.Routing;
using Xunit;

namespace Hardened.Web.Runtime.Tests.Routing;

/// <summary>
/// The runtime matcher, driven directly.
/// </summary>
/// <remarks>
/// Every semantic here is one the generated table already has, and the point of the file is that
/// the two agree: a path that only exists at run time has to route the way the same path written as
/// an attribute would. Where a case is subtle the comment names the generated table's behaviour it
/// is reproducing.
/// </remarks>
public class RuntimeRouteTableTests
{
    [Fact]
    public void LiteralPathMatches()
    {
        var table = Table(("/orders", "GET"));

        Assert.Equal("GET /orders", Named(table.Match("/orders", "GET")));
        Assert.Null(table.Match("/order", "GET"));
        Assert.Null(table.Match("/orders/1", "GET"));
    }

    [Fact]
    public void RootMatches()
    {
        var table = Table(("/", "GET"));

        Assert.Equal("GET /", Named(table.Match("/", "GET")));
    }

    [Fact]
    public void TokenBindsByName()
    {
        var table = Table(("/orders/{id}", "GET"));
        var matched = table.Match("/orders/42", "GET");

        Assert.Equal("GET /orders/{id}", Named(matched));
        Assert.Equal("42", matched!.PathTokens.Get("id").ToString());
    }

    [Fact]
    public void SeveralTokensBindInOrder()
    {
        var table = Table(("/users/{userId}/posts/{postId}", "GET"));
        var matched = table.Match("/users/7/posts/9", "GET");

        Assert.Equal("7", matched!.PathTokens.Get("userId").ToString());
        Assert.Equal("9", matched.PathTokens.Get("postId").ToString());
    }

    /// <remarks>
    /// The whole reason a generated handler can be served at a path it was not compiled with: the
    /// binder reads the token called <c>id</c> rather than token 0.
    /// </remarks>
    [Fact]
    public void TokenNamesComeFromTheRouteThatMatched()
    {
        var table = Table(("/a/{id}", "GET"), ("/b/{code}", "GET"));

        Assert.Equal("1", table.Match("/a/1", "GET")!.PathTokens.Get("id").ToString());
        Assert.Equal("", table.Match("/a/1", "GET")!.PathTokens.Get("code").ToString());
        Assert.Equal("2", table.Match("/b/2", "GET")!.PathTokens.Get("code").ToString());
    }

    [Fact]
    public void LiteralBeatsToken()
    {
        var table = Table(("/orders/latest", "GET"), ("/orders/{id}", "GET"));

        Assert.Equal("GET /orders/latest", Named(table.Match("/orders/latest", "GET")));
        Assert.Equal("GET /orders/{id}", Named(table.Match("/orders/1", "GET")));
    }

    /// <remarks>
    /// A value that fails a constraint is not a match at all, so the walk is free to try the next
    /// alternative at the same position - which is what makes <c>{id:int}</c> a 404 rather than a
    /// 400.
    /// </remarks>
    [Fact]
    public void ConstraintSelectsBetweenAlternatives()
    {
        var table = Table(("/items/{id:int}", "GET"), ("/items/{name:slug}", "GET"));

        Assert.Equal("GET /items/{id:int}", Named(table.Match("/items/7", "GET")));
        Assert.Equal("GET /items/{name:slug}", Named(table.Match("/items/blue-hat", "GET")));
        Assert.Null(table.Match("/items/Not_A_Slug", "GET"));
    }

    [Fact]
    public void ConstrainedTokenIsTriedBeforeAnUnconstrainedOne()
    {
        var table = Table(("/items/{anything}", "GET"), ("/items/{id:int}", "GET"));

        Assert.Equal("GET /items/{id:int}", Named(table.Match("/items/7", "GET")));
        Assert.Equal("GET /items/{anything}", Named(table.Match("/items/seven", "GET")));
    }

    [Fact]
    public void ConstraintArgumentsAreRead()
    {
        var table = Table(("/pages/{page:int:min(1)}", "GET"));

        Assert.Equal("GET /pages/{page:int:min(1)}", Named(table.Match("/pages/1", "GET")));
        Assert.Null(table.Match("/pages/0", "GET"));
    }

    [Fact]
    public void BacktracksPastAConstraintToAnotherBranch()
    {
        var table = Table(("/{id:int}/detail", "GET"), ("/{slug}/summary", "GET"));

        Assert.Equal("GET /{id:int}/detail", Named(table.Match("/7/detail", "GET")));
        Assert.Equal("GET /{slug}/summary", Named(table.Match("/7/summary", "GET")));
    }

    [Fact]
    public void MethodNotAllowedCarriesTheVerbsThePathAnswers()
    {
        var table = Table(("/orders", "GET"), ("/orders", "POST"));
        var matched = table.Match("/orders", "DELETE");

        Assert.NotNull(matched);
        Assert.Null(matched!.Handler);
        Assert.Equal("GET, POST, HEAD", matched.Allow);
    }

    /// <remarks>
    /// The routing table sends a HEAD to the GET handler and
    /// <c>WebExecutionHandlerService</c> drops the body on the way out, which is what makes the
    /// headers the same as the GET's.
    /// </remarks>
    [Fact]
    public void HeadReachesGet()
    {
        var table = Table(("/orders", "GET"));

        Assert.Equal("GET /orders", Named(table.Match("/orders", "HEAD")));
    }

    [Fact]
    public void HeadReachesTheSameHandlerInstanceAsGet()
    {
        var table = Table(("/orders", "GET"));

        Assert.Same(
            table.Match("/orders", "GET")!.Handler,
            table.Match("/orders", "HEAD")!.Handler
        );
    }

    [Fact]
    public void AnExplicitHeadWins()
    {
        var table = Table(("/orders", "GET"), ("/orders", "HEAD"));

        Assert.Equal("HEAD /orders", Named(table.Match("/orders", "HEAD")));
    }

    [Fact]
    public void MethodsAreComparedOrdinally()
    {
        var table = Table(("/orders", "GET"));

        Assert.Null(table.Match("/orders", "get")!.Handler);
    }

    [Fact]
    public void CatchAllTakesTheRestOfThePath()
    {
        var table = Table(("/assets/{*path}", "GET"));
        var matched = table.Match("/assets/css/site.css", "GET");

        Assert.Equal("GET /assets/{*path}", Named(matched));
        Assert.Equal("css/site.css", matched!.PathTokens.Get("path").ToString());
    }

    /// <remarks>
    /// A token names at least one character, catch-alls included: <c>{*name}</c> means the rest of
    /// the path, and <c>/assets/</c> has no rest. It used to bind the token to "" and answer 400
    /// about a URL that addresses no endpoint at all.
    /// </remarks>
    [Fact]
    public void ATokenNamesAtLeastOneCharacter()
    {
        Assert.Null(Table(("/assets/{*path}", "GET")).Match("/assets/", "GET"));
        Assert.Null(Table(("/orders/{id}", "GET")).Match("/orders/", "GET"));
    }

    /// <remarks>
    /// The node for <c>orders</c> exists only to be passed through. Reaching it as the last segment
    /// is not a match, and the walk has to say so rather than answer from a node with no route on
    /// it.
    /// </remarks>
    [Fact]
    public void ASegmentThatOnlyLeadsSomewhereIsNotAMatch()
    {
        var table = Table(("/orders/lines", "GET"));

        Assert.Null(table.Match("/orders", "GET"));
    }

    [Fact]
    public void ATokenDoesNotCrossASeparator()
    {
        Assert.Null(Table(("/orders/{id}", "GET")).Match("/orders/1/lines", "GET"));
    }

    [Fact]
    public void ALiteralRouteBeatsACatchAll()
    {
        var table = Table(("/assets/{*path}", "GET"), ("/assets/logo.svg", "GET"));

        Assert.Equal("GET /assets/logo.svg", Named(table.Match("/assets/logo.svg", "GET")));
        Assert.Equal("GET /assets/{*path}", Named(table.Match("/assets/other.svg", "GET")));
    }

    /// <remarks>
    /// <c>/orders</c> and <c>/orders/</c> are unrelated routes and always have been. Softening that
    /// is <c>TrailingSlash</c>, which <c>WebExecutionHandlerService</c> applies above every provider
    /// - so it applies to a registered route without this knowing about it.
    /// </remarks>
    [Fact]
    public void ATrailingSlashIsADifferentPath()
    {
        var table = Table(("/orders", "GET"));

        Assert.Null(table.Match("/orders/", "GET"));
    }

    [Fact]
    public void CaseSensitiveByDefault()
    {
        var table = Table(("/Orders", "GET"));

        Assert.Null(table.Match("/orders", "GET"));
        Assert.Equal("GET /Orders", Named(table.Match("/Orders", "GET")));
    }

    [Fact]
    public void CaseInsensitiveWhenTheEntryPointSaysSo()
    {
        var table = Table(true, ("/Orders/{id}", "GET"));

        Assert.Equal("GET /Orders/{id}", Named(table.Match("/orders/7", "GET")));
        Assert.Equal("7", table.Match("/ORDERS/7", "GET")!.PathTokens.Get("id").ToString());
    }

    [Fact]
    public void AnEmptyTableMatchesNothing()
    {
        Assert.Null(RuntimeRouteTable.Empty.Match("/orders", "GET"));
    }

    [Fact]
    public void APathThatIsNotAPathMatchesNothing()
    {
        var table = Table(("/orders", "GET"));

        Assert.Null(table.Match("orders", "GET"));
        Assert.Null(table.Match("", "GET"));
    }

    [Fact]
    public void ManyLiteralPathsShareTheOneProbe()
    {
        var routes = new (string, string)[50];

        for (var index = 0; index < routes.Length; index++)
        {
            routes[index] = ("/tenant-" + index + "/orders", "GET");
        }

        var table = Table(routes);

        Assert.Equal("GET /tenant-0/orders", Named(table.Match("/tenant-0/orders", "GET")));
        Assert.Equal("GET /tenant-49/orders", Named(table.Match("/tenant-49/orders", "GET")));
        Assert.Null(table.Match("/tenant-50/orders", "GET"));
    }

    [Fact]
    public void TheHandlerIsBuiltOnceAndOnlyWhenTheRouteIsReached()
    {
        var built = 0;
        var builder = new RuntimeRouteTableBuilder();

        Assert.True(
            builder.TryAdd(
                "/orders",
                "GET",
                path =>
                {
                    built++;

                    return Handler("GET /orders");
                },
                out _
            )
        );

        var table = builder.Build();

        Assert.Equal(0, built);

        var first = table.Match("/orders", "GET")!.Handler;

        Assert.Same(first, table.Match("/orders", "GET")!.Handler);
        Assert.Equal(1, built);
    }

    /// <remarks>
    /// The handler is constructed with the path it is served at, which is what
    /// <c>ExecutionRequestHandlerInfo.WithPath</c> exists for. Without it a handler registered at
    /// three paths would report one of them to every filter, every convention and every log line.
    /// </remarks>
    [Fact]
    public void TheHandlerIsToldWhatPathItIsServedAt()
    {
        var builder = new RuntimeRouteTableBuilder();

        Assert.True(builder.TryAdd("/tenant-7/orders/{id}", "GET", Handler, out _));

        var table = builder.Build();

        Assert.Equal(
            "/tenant-7/orders/{id}",
            table.Match("/tenant-7/orders/1", "GET")!.Handler!.HandlerInfo.Path
        );
    }

    // ---- registration failures ----------------------------------------------

    [Theory]
    [InlineData("orders", "start with '/'")]
    [InlineData("", "cannot be empty")]
    [InlineData("/v{major}/orders", "shares a segment")]
    [InlineData("/orders/{}", "no name")]
    [InlineData("/orders/{id?}", "optional")]
    [InlineData("/orders/{id=5}", "default")]
    [InlineData("/orders/{id", "shares a segment")]
    [InlineData("/{id}/orders/{id}", "more than once")]
    [InlineData("/{*rest}/orders", "catch-all")]
    [InlineData("/orders//lines", "empty segment")]
    [InlineData("/orders/{id:isbn}", "nothing declares a route constraint")]
    [InlineData("/orders/{id:int(3)}", "takes no arguments")]
    [InlineData("/orders/{id:range(3)}", "does not take 1")]
    [InlineData("/orders/{id:min(x)}", "whole numbers")]
    [InlineData("/orders/{a{b}", "nested brace")]
    [InlineData("/orders/{id:}", "empty constraint")]
    public void ATemplateThatIsNotARouteIsReported(string template, string expected)
    {
        var builder = new RuntimeRouteTableBuilder();

        Assert.False(builder.TryAdd(template, "GET", Handler, out var error));
        Assert.Contains(expected, error);
    }

    [Fact]
    public void ARouteWithNoVerbIsReported()
    {
        var builder = new RuntimeRouteTableBuilder();

        Assert.False(builder.TryAdd("/orders", "", Handler, out var error));
        Assert.Contains("registered with no verb", error);
    }

    [Fact]
    public void TwoRoutesAtOnePathUnderOneVerbAreReported()
    {
        var builder = new RuntimeRouteTableBuilder();

        Assert.True(builder.TryAdd("/orders/{id:int}", "GET", Handler, out _));
        Assert.False(builder.TryAdd("/orders/{orderId:int}", "GET", Handler, out var error));
        Assert.Contains("could never be reached", error);
    }

    [Fact]
    public void TheSamePathUnderTwoVerbsIsNotAmbiguous()
    {
        var builder = new RuntimeRouteTableBuilder();

        Assert.True(builder.TryAdd("/orders/{id}", "GET", Handler, out _));
        Assert.True(builder.TryAdd("/orders/{id}", "DELETE", Handler, out _));
    }

    [Fact]
    public void ADeclaredConstraintIsCalled()
    {
        var builder = new RuntimeRouteTableBuilder(
            constraints: new Dictionary<string, RouteConstraintTest>
            {
                { "isbn", static value => value.Length == 13 },
            }
        );

        Assert.True(builder.TryAdd("/books/{code:isbn}", "GET", Handler, out _));

        var table = builder.Build();

        Assert.NotNull(table.Match("/books/9780306406157", "GET"));
        Assert.Null(table.Match("/books/short", "GET"));
    }

    // ---- helpers ------------------------------------------------------------

    private static RuntimeRouteTable Table(params (string Template, string Method)[] routes) =>
        Table(false, routes);

    private static RuntimeRouteTable Table(
        bool caseInsensitive,
        params (string Template, string Method)[] routes
    )
    {
        var builder = new RuntimeRouteTableBuilder(caseInsensitive);

        foreach (var route in routes)
        {
            Assert.True(
                builder.TryAdd(
                    route.Template,
                    route.Method,
                    _ => Handler(route.Method + " " + route.Template),
                    out var error
                ),
                error
            );
        }

        return builder.Build();
    }

    private static string? Named(RequestHandlerInfo? matched) =>
        ((StubHandler?)matched?.Handler)?.Name;

    private static IExecutionRequestHandler Handler(string name) => new StubHandler(name, name);

    /// <summary>
    /// Stands in for a generated handler. The matcher never runs one - it hands the handler to
    /// <c>WebExecutionHandlerService</c>, which builds the chain - so a stub is the whole of what
    /// these tests need.
    /// </summary>
    private sealed class StubHandler : IExecutionRequestHandler
    {
        public StubHandler(string name, string path)
        {
            Name = name;
            HandlerInfo = new ExecutionRequestHandlerInfo(
                path,
                "GET",
                typeof(StubHandler),
                nameof(Name)
            );
        }

        public string Name { get; }

        public IExecutionRequestHandlerInfo HandlerInfo { get; }

        public IExecutionChain GetExecutionChain(IExecutionContext context) =>
            throw new NotSupportedException();
    }
}
