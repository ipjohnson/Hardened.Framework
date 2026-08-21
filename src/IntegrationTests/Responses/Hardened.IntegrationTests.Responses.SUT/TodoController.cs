using Hardened.Requests.Abstract.Responses;
using Hardened.Web.Runtime.Attributes;

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
}

public record ApiError(string Code, string Message);
