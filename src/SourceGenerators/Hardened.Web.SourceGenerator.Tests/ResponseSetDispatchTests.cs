using Hardened.Requests.Abstract.Responses;
using Hardened.SourceGeneration.Testing;
using Hardened.Web.Runtime.Attributes;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests;

/// <summary>
/// The dispatch emitted for a handler that returns a declared response set.
///
/// <para>
/// Every case here compiles the generated trees rather than only reading them, for the reason
/// <c>GeneratedCodeCompilesTests</c> exists: a generator test that asserts on reported diagnostics
/// says nothing about whether the C# it wrote builds, and three defects shipped that way. A switch
/// over case types is exactly the shape where a missing <c>break</c>, an unbound pattern variable
/// or an unqualified name produces text that reads correctly and does not compile.
/// </para>
/// </summary>
public class ResponseSetDispatchTests {

    private static readonly Type[] Anchors = [
        typeof(GetAttribute),
        typeof(Response<,>)
    ];

    private static GeneratorResult Generate(string source) =>
        GeneratorTestHarness.Run(source, new WebLibrarySourceGenerator(), Anchors);

    /// <summary>
    /// A handler declaring a success type and a 404, which is the shape the whole feature exists
    /// for.
    /// </summary>
    private const string TwoCaseHandler =
        """
        using Hardened.Requests.Abstract.Responses;
        using Hardened.Web.Runtime.Attributes;

        namespace TestApp;

        public record Todo(int Id, string Title);

        public class TodoController {
            [Get("/todos/{id}")]
            public Response<Todo, NotFound> GetTodo(int id) =>
                id > 0 ? new Todo(id, "a todo") : new NotFound("todo");
        }
        """;

    /// <summary>
    /// The generated handler, found by the method it carries rather than by its file name - the
    /// naming scheme is not what these are about, and the routing table has switches of its own
    /// that an assertion here must not accidentally read.
    /// </summary>
    private static string Handler(GeneratorResult result) =>
        result.GeneratedSources
            .First(pair => pair.Value.Contains("InvokeMethod", StringComparison.Ordinal))
            .Value;

    #region it compiles

    [Fact]
    public void AResponseSetHandlerCompiles() {
        Generate(TwoCaseHandler).AssertNoErrors();
    }

    /// <summary>
    /// A case that contributes headers reaches <c>ApplyHeaders</c> through the pattern variable, so
    /// the binding has to be emitted and typed. This is the arm most likely to produce
    /// uncompilable text.
    /// </summary>
    [Fact]
    public void AResponseSetWithAHeaderContributingCaseCompiles() {
        Generate("""
            using Hardened.Requests.Abstract.Responses;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            public record Todo(int Id);

            public class TodoController {
                [Get("/todos/{id}")]
                public Response<Todo, Unauthorized, RateLimited> GetTodo(int id) => new Todo(id);
            }
            """).AssertNoErrors();
    }

    /// <summary>
    /// Async is the ordinary case - a handler returns <c>Task&lt;Response&lt;...&gt;&gt;</c> - and
    /// the selector has to unwrap past the task to find the set.
    /// </summary>
    [Fact]
    public void AnAsyncResponseSetHandlerCompiles() {
        Generate("""
            using System.Threading.Tasks;
            using Hardened.Requests.Abstract.Responses;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            public record Todo(int Id);

            public class TodoController {
                [Get("/todos/{id}")]
                public Task<Response<Todo, NotFound>> GetTodo(int id) =>
                    Task.FromResult<Response<Todo, NotFound>>(new Todo(id));
            }
            """).AssertNoErrors();
    }

    /// <summary>
    /// Eight cases is the shipped cap, so it is the arity least likely to be exercised and most
    /// likely to be wrong while it is.
    /// </summary>
    [Fact]
    public void TheHighestArityCompiles() {
        Generate("""
            using System;
            using Hardened.Requests.Abstract.Responses;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            public record Todo(int Id);

            public class TodoController {
                [Get("/todos/{id}")]
                public Response<Todo, NotFound, Conflict, Gone, PreconditionFailed, Forbidden,
                                Unauthorized, RateLimited> GetTodo(int id) => new Todo(id);
            }
            """).AssertNoErrors();
    }

    /// <summary>
    /// A bodyless case sits beside ones that have a body, so the arm that suppresses serialization
    /// has to be per-case rather than per-handler.
    /// </summary>
    [Fact]
    public void ABodylessCaseCompiles() {
        Generate("""
            using Hardened.Requests.Abstract.Responses;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            public class TodoController {
                [Delete("/todos/{id}")]
                public Response<NoContent, NotFound> Delete(int id) => new NoContent();
            }
            """).AssertNoErrors();
    }

    #endregion

    #region what it emits

    /// <summary>
    /// The payload is assigned once rather than in every arm: <c>ResponseValue</c> is already
    /// <c>object?</c> and a union's <c>Value</c> is already <c>object?</c>, so nothing transforms it.
    /// </summary>
    [Fact]
    public void ThePayloadIsAssignedOnceFromValue() {
        var handler = Handler((Generate(TwoCaseHandler)));

        Assert.Contains("Response.ResponseValue = ", handler, StringComparison.Ordinal);
        Assert.Contains(".Value", handler, StringComparison.Ordinal);
    }

    /// <summary>
    /// The status of each case, which is the whole point: the 404 comes from the case type's own
    /// <c>[HttpStatus]</c> and the success case from the endpoint's success status.
    /// </summary>
    [Fact]
    public void EachCaseGetsItsOwnStatus() {
        var handler = Handler((Generate(TwoCaseHandler)));

        Assert.Contains("Response.Status = 404", handler, StringComparison.Ordinal);
        Assert.Contains("Response.Status = 200", handler, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>[Get(SuccessStatus = 201)]</c> moves the unannotated case rather than the annotated one.
    /// Hardcoding 200 would be wrong for every POST that creates.
    /// </summary>
    [Fact]
    public void AnUnannotatedCaseTakesTheEndpointSuccessStatus() {
        var handler = Handler((Generate("""
            using Hardened.Requests.Abstract.Responses;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            public record Todo(int Id);

            public class TodoController {
                [Post("/todos", SuccessStatus = 201)]
                public Response<Todo, Conflict> Create() => new Todo(1);
            }
            """)));

        Assert.Contains("Response.Status = 201", handler, StringComparison.Ordinal);
        Assert.Contains("Response.Status = 409", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("Response.Status = 200", handler, StringComparison.Ordinal);
    }

    /// <summary>
    /// Only where there is something to apply. A type test per response would be the cost the
    /// compile-time switch exists to avoid.
    /// </summary>
    [Fact]
    public void OnlyAHeaderContributingCaseCallsApplyHeaders() {
        var withHeaders = Handler((Generate("""
            using Hardened.Requests.Abstract.Responses;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            public record Todo(int Id);

            public class TodoController {
                [Get("/todos/{id}")]
                public Response<Todo, RateLimited> GetTodo(int id) => new Todo(id);
            }
            """)));

        Assert.Contains("ApplyHeaders", withHeaders, StringComparison.Ordinal);

        // Neither Todo nor NotFound contributes a header.
        Assert.DoesNotContain(
            "ApplyHeaders", Handler((Generate(TwoCaseHandler))), StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>return default;</c> compiles and leaves Value null, so the switch has to answer it. A
    /// success status there would send an empty body under a 200.
    /// </summary>
    [Fact]
    public void TheDefaultArmAnswersFiveHundred() {
        var handler = Handler((Generate(TwoCaseHandler)));

        Assert.Contains("default:", handler, StringComparison.Ordinal);
        Assert.Contains("Response.Status = 500", handler, StringComparison.Ordinal);
    }

    /// <summary>
    /// A handler returning one type keeps the assignment it always had. This is the path every
    /// application in existence takes, and the feature is worth nothing if it disturbed it.
    /// </summary>
    [Fact]
    public void AnOrdinaryHandlerIsUnchanged() {
        var handler = Handler((Generate("""
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            public class TodoController {
                [Get("/todos/{id}")]
                public string GetTodo(int id) => "todo";
            }
            """)));

        // Not "no switch" - the generated parameters class has one over its indexer. What an
        // ordinary handler must not have is a status decided per case, which is the whole of what
        // the response-set dispatch adds.
        Assert.DoesNotContain("Response.Status = ", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("Response.ShouldSerialize", handler, StringComparison.Ordinal);
        Assert.Contains("Response.ResponseValue", handler, StringComparison.Ordinal);
    }

    #endregion
}
