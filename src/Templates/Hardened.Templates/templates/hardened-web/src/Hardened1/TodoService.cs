using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Abstract.Responses;
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
/// Every method awaits the store and returns the value itself - a Todo, a NotFound - which the
/// compiler converts into the type the signature names. Nothing here wraps a value in a Task; the
/// store does that, once, for the in-memory case.
///
/// Build, then read obj/<configuration>/<tfm>/openapi/generated/ to see the interface, the models, the
/// routing table and the validation the contract's constraints produced.
/// </remarks>
[Handler]
public class TodoService(ITodoStore store) : ITodosService {

#if (throwsMode)
    // The body a thrown error carries. An OpenAPI description declares a shared Problem schema for
    // every error; a Smithy model declares a named @error structure per failure. Only a throw builds
    // one by hand - a returned case is built by the conversion the build writes.
#if (openapi)
    private static Problem NotFoundBody(string detail) =>
        new() { Type = "about:blank", Title = "Not Found", Status = 404, Detail = detail };
#endif
#if (smithy)
    private static TodoNotFound NotFoundBody(string message) => new(message);

    private static TodoTitleTaken ConflictBody(string message) => new(message);
#endif

#endif
    /// <summary>
    /// Every todo, as the array the contract declares.
    /// </summary>
    /// <remarks>
    /// Outside the response-model split below, because this operation declares one status and there
    /// is nothing for a declared set to hold. A named list is not a C# type of its own in either
    /// contract language, so both generate List&lt;Todo&gt; and this method is written once.
    /// </remarks>
    public async Task<List<Todo>> ListTodos() => (await store.All()).ToList();

#if (throwsMode)
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
    public Task<Todo?> GetTodo(int id) => store.Find(id);

#if (openapi)
    /// <summary>
    /// A 201 carrying the Location the contract declares, or a 409.
    /// </summary>
    /// <remarks>
    /// The one operation here that names a response set in throws mode, and the contract is why:
    /// its 201 declares a Location, and a returned Todo has nowhere to put one - Todo is the type
    /// GetTodo answers with too, so a header on it would go out on every read. Declaring a header is
    /// the only thing that does this. Every other operation below declares none and keeps the bare
    /// return type that throws mode is chosen for.
    ///
    /// The 409 is a case rather than a throw for the same reason: once an operation has a set, the
    /// set is where all of its statuses live.
    /// </remarks>
    public async Task<CreateTodoResponse> CreateTodo(NewTodo body) {
        if (await store.TitleExists(body.Title)) {
            // The framework's own Conflict, converted by the build into the case the contract
            // declares: its Problem, with type, title and status filled from the record and the
            // detail from here.
            return new Conflict($"A todo titled '{body.Title}' already exists.");
        }

        var created = await store.Add(body.Title);

        // Routes is generated from the same contract, so this link cannot drift from the route it
        // points at - rename the path in the contract and this stops compiling.
        return new CreateTodoCreated(created, TemplateModuleNameLibrary.Routes.Todos.GetTodo(created.Id));
    }
#endif
#if (smithy)
    /// <summary>
    /// 409 by throwing, because this operation declares one success and the signature names it.
    /// </summary>
    public async Task<Todo> CreateTodo(NewTodo body) {
        if (await store.TitleExists(body.Title)) {
            throw ConflictBody($"A todo titled '{body.Title}' already exists.").AsException();
        }

        return await store.Add(body.Title);
    }
#endif

    /// <summary>204 on success - the contract declares no body, so the signature has no result.</summary>
    /// <remarks>
    /// AsException() is the whole of the throw in both languages. It is the framework's own verb for
    /// turning a response into a thrown one, and the build generates the overload that reaches a
    /// declared error's body - so the type is named once rather than beside the body it carries.
    /// </remarks>
    public async Task RemoveTodo(int id) {
        if (!await store.Remove(id)) {
#if (openapi)
            throw new NotFound<Problem>(NotFoundBody($"No todo has id {id}.")).AsException();
#endif
#if (smithy)
            throw NotFoundBody($"No todo has id {id}.").AsException();
#endif
        }
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
    ///
    /// The 404 is the framework's own NotFound. The build wrote the conversion into the case the
    /// contract declares - the declared body with type, title and status filled from the record and
    /// the detail from here - so a handler says why and nothing else. NotFound.Default is the same
    /// answer with a generic detail, shared, for a handler with nothing to add.
    /// </remarks>
    public async Task<GetTodoResponse> GetTodo(int id) {
        var todo = await store.Find(id);

        if (todo is null) {
            return new NotFound("todo", $"No todo has id {id}.");
        }

        return todo;
    }

    /// <summary>201 with the new todo, or 409.</summary>
    public async Task<CreateTodoResponse> CreateTodo(NewTodo body) {
        if (await store.TitleExists(body.Title)) {
            return new Conflict($"A todo titled '{body.Title}' already exists.");
        }

        var created = await store.Add(body.Title);

#if (openapi)
        // The 201 declares a Location, so its case carries one beside the payload. Routes is
        // generated from the same contract, so the link cannot drift from the route it points at.
        return new CreateTodoCreated(created, TemplateModuleNameLibrary.Routes.Todos.GetTodo(created.Id));
#endif
#if (smithy)
        return created;
#endif
    }

    /// <summary>
    /// 204 or 404, and the 204 is a case like any other.
    /// </summary>
    /// <remarks>
    /// A bodyless success has no schema to name it, so the build generates a case type carrying
    /// nothing. Before that existed this operation's response set held only the 404 and there was
    /// no way to say it had worked.
    /// </remarks>
    public async Task<RemoveTodoResponse> RemoveTodo(int id) {
        if (!await store.Remove(id)) {
            return new NotFound("todo", $"No todo has id {id}.");
        }

        return new RemoveTodoNoContent();
    }
#endif
}
