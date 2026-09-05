using DependencyModules.Runtime.Attributes;
#if (specFirst)
using Hardened1.Models;
#endif

namespace Hardened1;

#if (codeFirst)
/// <summary>A todo, as it goes over the wire.</summary>
public record Todo(int Id, string Title, bool Done);

/// <summary>
/// What a client sends to create one.
/// </summary>
/// <remarks>
/// Separate from <see cref="Todo"/> because the server assigns the id, and a request model carrying
/// one invites a client to choose it.
/// </remarks>
public record NewTodo(string Title);

#endif
/// <summary>
/// Where the todos live.
/// </summary>
/// <remarks>
/// Task-based, because a store is where a real implementation waits on I/O. The handlers await it
/// and return their answers directly; the one in-memory implementation below wraps its answers in
/// Task.FromResult, so that wrapping is written here once rather than around every return.
///
/// An interface, and not only for testability. A handler parameter is bound from the request unless
/// the generator can tell it is a service, and an unattributed concrete class is taken as the body -
/// so injecting TodoStore directly generates DeserializeRequestBody&lt;TodoStore&gt;, and on a route
/// that also takes a real body, two of them. [FromServices] says the same thing explicitly.
/// </remarks>
public interface ITodoStore {

    Task<IReadOnlyList<Todo>> All();

    Task<Todo?> Find(int id);

    Task<bool> TitleExists(string title);

    Task<Todo> Add(string title);

    Task<bool> Remove(int id);
}

/// <summary>
/// In memory, because the point of the sample is the request pipeline rather than storage.
/// </summary>
/// <remarks>
/// [SingletonService] registers this against every interface it implements and against the class
/// itself. The module lists nothing, so registration cannot fall out of step with what exists -
/// which is also what lets a test replace it with [Mock] without changing any wiring here.
#if (specFirst)
///
/// Todo and NewTodo are not declared here: the contract declares them, and the build writes them
/// into Hardened1.Models. Declaring a second pair beside the generated ones is CS0101 on both.
#endif
/// </remarks>
[SingletonService]
public class TodoStore : ITodoStore {

    private readonly Dictionary<int, Todo> _todos = new() {
        [1] = new Todo(1, "Read the generated code", true),
        [2] = new Todo(2, "Add an endpoint", false)
    };

    private int _nextId = 3;

    public Task<IReadOnlyList<Todo>> All() =>
        Task.FromResult<IReadOnlyList<Todo>>(_todos.Values.ToList());

    public Task<Todo?> Find(int id) =>
        Task.FromResult(_todos.TryGetValue(id, out var todo) ? todo : null);

    /// <summary>Titles are unique, which is what gives the sample a real 409.</summary>
    public Task<bool> TitleExists(string title) =>
        Task.FromResult(
            _todos.Values.Any(todo => string.Equals(todo.Title, title, StringComparison.OrdinalIgnoreCase)));

    public Task<Todo> Add(string title) {
        var todo = new Todo(_nextId++, title, false);

        _todos[todo.Id] = todo;

        return Task.FromResult(todo);
    }

    public Task<bool> Remove(int id) => Task.FromResult(_todos.Remove(id));
}
