using Hardened.Requests.Abstract.Responses;
using Hardened.Web.Runtime.Attributes;

namespace Hardened.IntegrationTests.Union.SUT;

public record Todo(int Id, string Title);

public record NewTodo(string Title);

/// <summary>
/// The declared sets, as C# 15 language unions.
/// </summary>
/// <remarks>
/// Per-status wrapper types rather than bare payloads, for the reason that applies to the struct
/// form equally: two identical case types give two identical conversions and the compiler rejects
/// the use site. NotFound appearing in two different unions is fine; twice in one is not.
/// </remarks>
public union TodoResult(Todo, NotFound);

public union NewTodoResult(Created<Todo>, Conflict);

public union RemovedTodoResult(NoContent, NotFound);

/// <summary>
/// The same handlers as the Responses fixture, with the keyword instead of the struct.
/// </summary>
/// <remarks>
/// <para>
/// Hardened matches both structurally - a public single-parameter constructor per case and a public
/// object? Value - so this and its sibling should dispatch identically. That claim had never been
/// checked against a running application: UNION-RESPONSES-PLAN.md Part 9 experiment 2 asked whether
/// a keyword-declared union is recognised at all, and answered it only from generated text.
/// </para>
/// <para>
/// If the structural match ever stops recognising the keyword, these fail and the sibling passes,
/// which is what makes the pair worth having rather than either alone.
/// </para>
/// </remarks>
public class TodoController {

    [Get("/{id}")]
    public TodoResult ById(int id) {
        if (id == 404) {
            return new NotFound("todo", "no todo has that id");
        }

        return new Todo(id, "declared");
    }

    [Post("/")]
    public NewTodoResult Create(NewTodo request) {
        if (request.Title == "taken") {
            return new Conflict("a todo with that title exists");
        }

        var todo = new Todo(7, request.Title);

        return new Created<Todo>(todo, $"/union/{todo.Id}");
    }

    [Delete("/{id}")]
    public RemovedTodoResult Remove(int id) {
        if (id == 404) {
            return new NotFound("todo", "no todo has that id");
        }

        return new NoContent();
    }
}
