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
/// An interface, and not only for testability. A handler parameter is bound from the request unless
/// the generator can tell it is a service, and an unattributed concrete class is taken as the body -
/// so injecting TodoStore directly generates DeserializeRequestBody&lt;TodoStore&gt;, and on a route
/// that also takes a real body, two of them. [FromServices] says the same thing explicitly.
/// </remarks>
public interface ITodoStore {

    IReadOnlyList<Todo> All();

    Todo? Find(int id);

    bool TitleExists(string title);

    Todo Add(string title);

    bool Remove(int id);
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

    public IReadOnlyList<Todo> All() => _todos.Values.ToList();

    public Todo? Find(int id) => _todos.TryGetValue(id, out var todo) ? todo : null;

    /// <summary>Titles are unique, which is what gives the sample a real 409.</summary>
    public bool TitleExists(string title) =>
        _todos.Values.Any(todo => string.Equals(todo.Title, title, StringComparison.OrdinalIgnoreCase));

    public Todo Add(string title) {
        var todo = new Todo(_nextId++, title, false);

        _todos[todo.Id] = todo;

        return todo;
    }

    public bool Remove(int id) => _todos.Remove(id);
}
