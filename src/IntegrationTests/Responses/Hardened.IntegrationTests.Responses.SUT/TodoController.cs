using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Responses;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Responses;

namespace Hardened.IntegrationTests.Responses.SUT;

public record Todo(int Id, string Title);

public record NewTodo(string Title);

/// <summary>
/// Handlers whose return type is the whole set of responses they can answer with.
/// </summary>
/// <remarks>
/// <para>
/// The code-first half of <c>ResponseModel.Response</c>, exercised through a real request rather
/// than through the generated text. Nothing did that before: the emitter tests assert what is
/// written and the parser tests assert the model, so a dispatch that recognised the set and then
/// sent the wrapper - which is exactly what the specification-first path did - passed every one of
/// them.
/// </para>
/// <para>
/// <c>Response&lt;T1..Tn&gt;</c> rather than a union, so this fixture builds on any compiler. The
/// keyword's own coverage is the sibling Union fixture, which needs the .NET 11 SDK.
/// </para>
/// </remarks>
public class TodoController {

    /// <summary>200 or 404, both named in the signature.</summary>
    [Get("/{id}")]
    public Response<Todo, NotFound> ById(int id) {
        if (id == 404) {
            return new NotFound("todo", "no todo has that id");
        }

        return new Todo(id, "declared");
    }

    /// <summary>
    /// 201 with a Location header, or 409.
    /// </summary>
    /// <remarks>
    /// The status comes from the case rather than from anything this method does, and
    /// <c>Created&lt;T&gt;</c> carries the body and the header together - so the wire receives the
    /// Todo, not the wrapper that named the status.
    /// </remarks>
    [Post("/")]
    public Response<Created<Todo>, Conflict> Create(NewTodo request) {
        if (request.Title == "taken") {
            return new Conflict("a todo with that title exists");
        }

        var todo = new Todo(7, request.Title);

        return new Created<Todo>(todo, $"/responses/{todo.Id}");
    }

    /// <summary>
    /// 204 or 404.
    /// </summary>
    /// <remarks>
    /// The bodyless case. <c>NoContent</c> serialises nothing, which is what distinguishes a 204
    /// from a 200 carrying the four characters "null".
    /// </remarks>
    [Delete("/{id}")]
    public Response<NoContent, NotFound> Remove(int id) {
        if (id == 404) {
            return new NotFound("todo", "no todo has that id");
        }

        return new NoContent();
    }

    /// <summary>
    /// A typed error body, which is the shape a real API uses.
    /// </summary>
    /// <remarks>
    /// <c>NotFound&lt;T&gt;</c> puts the <c>T</c> on the wire rather than the wrapper - a schema
    /// written from the wrapper would describe a shape no client ever receives.
    /// </remarks>
    [Get("/typed/{id}")]
    public Response<Todo, NotFound<ApiError>> Typed(int id) {
        if (id == 404) {
            return new NotFound<ApiError>(new ApiError("not_found", "no todo has that id"));
        }

        return new Todo(id, "declared");
    }

    #region returned on its own, with no set around it

    /// <summary>
    /// The same <c>Created&lt;Todo&gt;</c> as <see cref="Create"/>, returned on its own.
    /// </summary>
    /// <remarks>
    /// A handler with one outcome has no set to declare, and the value already states its status,
    /// its header and which of its members is the body. Until the serializer read that, this
    /// answered 200 with no <c>Location</c> and the wrapper on the wire - so the body carried
    /// <c>"status": 201</c> inside a 200.
    /// </remarks>
    [Post("/bare")]
    public Created<Todo> CreateBare(NewTodo request) =>
        new(new Todo(7, request.Title), "/responses/7");

    /// <summary>The identical value, thrown instead of returned.</summary>
    /// <remarks>
    /// The comparison that matters. <c>ResponseException</c> has read all three interfaces since it
    /// existed, so this path was always right and the returned one was not.
    /// </remarks>
    [Post("/bare-thrown")]
    public Todo CreateBareThrown(NewTodo request) =>
        throw new Created<Todo>(new Todo(7, request.Title), "/responses/7").AsException();

    /// <summary>A bodyless response returned on its own.</summary>
    /// <remarks>
    /// <c>NoContent</c> states <c>HasBody</c> false, which is what stops "null" being written into
    /// a 204 - the same thing the set's switch does through <c>ShouldSerialize</c>.
    /// </remarks>
    [Delete("/bare/{id}")]
    public NoContent RemoveBare(int id) => new();

    /// <summary>
    /// Writes a status, then returns a type that declares another.
    /// </summary>
    /// <remarks>
    /// The declared status wins. A handler that says <c>Created&lt;T&gt;</c> has said 201, and an
    /// earlier write is the contradiction rather than the override - which is what a set already
    /// does, and <see cref="CreateInSetOverridden"/> is the twin that proves the two agree rather
    /// than asserting it.
    /// </remarks>
    [Post("/bare-overridden")]
    public Created<Todo> CreateBareOverridden(IExecutionContext context, NewTodo request) {
        context.Response.Status = 202;

        return new Created<Todo>(new Todo(7, request.Title), "/responses/7");
    }

    /// <summary>The same contradiction inside a declared set.</summary>
    [Post("/set-overridden")]
    public Response<Created<Todo>, Conflict> CreateInSetOverridden(
        IExecutionContext context, NewTodo request) {
        context.Response.Status = 202;

        return new Created<Todo>(new Todo(7, request.Title), "/responses/7");
    }

    #endregion
}

public record ApiError(string Code, string Message);
