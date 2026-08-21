using Hardened.Requests.Abstract.Attributes;
using Hardened1.Models;
using Hardened1.Services;

namespace Hardened1;

/// <summary>
/// The implementation of a service interface the build wrote from the contract.
/// </summary>
/// <remarks>
/// There are no route attributes anywhere in this project - the verb, the path, the shapes and the
/// statuses all came from the contract, and the generated routing table points here. [Handler] is
/// what marks this class as the implementation to route to.
///
/// ITodosService and every model below it exist only after a build. Add an operation to the contract
/// and this class stops satisfying the interface, which is the point: the specification and the
/// code cannot drift apart without the build saying so.
///
/// Build, then read obj/Debug/net8.0/openapi/generated/ to see the interface, the models, the
/// routing table and the validation the contract's constraints produced.
/// </remarks>
[Handler]
public class TodoService : ITodosService {

    private readonly ITodoStore _store;

    public TodoService(ITodoStore store) {
        _store = store;
    }

    // The one thing the two contract languages do not agree on. An OpenAPI description declares a
    // shared Problem schema for every error; a Smithy model declares a named @error structure per
    // failure. The signatures either produces are identical, so isolating the body here keeps every
    // call site below the same in both.
#if (openapi)
    private static Problem NotFoundBody(string detail) => new() { Detail = detail };

    private static Problem ConflictBody(string detail) => new() { Detail = detail };
#endif
#if (smithy)
    private static TodoNotFound NotFoundBody(string message) => new(message);

    private static TodoTitleTaken ConflictBody(string message) => new(message);
#endif

#if (standardMode)
    /// <summary>
    /// Null is the 404.
    /// </summary>
    /// <remarks>
    /// The ? on the return type is generated from the contract declaring a 404 - it is how the
    /// document says a null answer is allowed here. Returning null answers 404 with the body the
    /// document declared for it. Without a declared 404 the ? is absent and the compiler says so.
    ///
    /// A handler that wants to explain the refusal throws the generated exception type instead,
    /// which carries a body it wrote. Null is the "nothing to add" answer.
    /// </remarks>
    public Task<Todo?> GetTodo(int id) =>
        Task.FromResult(_store.Find(id));

    /// <summary>
    /// 409 by throwing, because this operation declares one success and the signature names it.
    /// </summary>
    public Task<Todo> CreateTodo(NewTodo body) {
        if (_store.TitleExists(body.Title)) {
            throw new CreateTodoConflictException(
                ConflictBody($"A todo titled '{body.Title}' already exists."));
        }

        return Task.FromResult(_store.Add(body.Title));
    }

    /// <summary>204 on success - the contract declares no body, so the signature has no result.</summary>
    public Task RemoveTodo(int id) {
        if (!_store.Remove(id)) {
            throw new RemoveTodoNotFoundException(
                NotFoundBody($"No todo has id {id}."));
        }

        return Task.CompletedTask;
    }
#endif
#if (declaredMode)
    /// <summary>
    /// Every status the contract declares, in the return type.
    /// </summary>
    /// <remarks>
    /// The build generated one case per declared response and a container that holds exactly one of
    /// them, so there is no null to interpret and no exception to remember. Returning the wrong
    /// status for this operation is a compile error rather than a wrong answer.
    /// </remarks>
    public Task<GetTodoResponse> GetTodo(int id) {
        var todo = _store.Find(id);

        if (todo is null) {
            return Task.FromResult<GetTodoResponse>(
                new GetTodoNotFound(NotFoundBody($"No todo has id {id}.")));
        }

        return Task.FromResult<GetTodoResponse>(todo);
    }

    /// <summary>201 with the new todo, or 409.</summary>
    public Task<CreateTodoResponse> CreateTodo(NewTodo body) {
        if (_store.TitleExists(body.Title)) {
            return Task.FromResult<CreateTodoResponse>(
                new CreateTodoConflict(
                    ConflictBody($"A todo titled '{body.Title}' already exists.")));
        }

        return Task.FromResult<CreateTodoResponse>(_store.Add(body.Title));
    }

    /// <summary>
    /// 204 or 404, and the 204 is a case like any other.
    /// </summary>
    /// <remarks>
    /// A bodyless success has no schema to name it, so the build generates a case type carrying
    /// nothing. Before that existed this operation's response set held only the 404 and there was
    /// no way to say it had worked.
    /// </remarks>
    public Task<RemoveTodoResponse> RemoveTodo(int id) {
        if (!_store.Remove(id)) {
            return Task.FromResult<RemoveTodoResponse>(
                new RemoveTodoNotFound(NotFoundBody($"No todo has id {id}.")));
        }

        return Task.FromResult<RemoveTodoResponse>(new RemoveTodoNoContent());
    }
#endif
}
