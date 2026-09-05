using System.Collections.Concurrent;
using System.Reflection;

namespace Hardened.Web.Testing;

/// <summary>
/// The value a completed task carries, or null where it carries none.
/// </summary>
/// <remarks>
/// <para>
/// Reflection because <see cref="ClientAssertions.Returns{TExpected}"/> takes a <see cref="Task"/>.
/// C# infers all of a method's type arguments or none of them, so an extension on
/// <c>Task&lt;T&gt;</c> would make the call site name the body type twice - once inside
/// <c>Created&lt;Todo&gt;</c> and once beside it.
/// </para>
/// <para>
/// A method declared <c>async Task</c> completes as a <c>Task&lt;VoidTaskResult&gt;</c>, whose
/// result is a struct standing for no value at all. Reading it would hand a delete's nothing to an
/// expectation as a body.
/// </para>
/// </remarks>
internal static class TaskResult {

    private static readonly ConcurrentDictionary<Type, PropertyInfo?> Results = new();

    private static readonly Type? VoidResult =
        typeof(Task).Assembly.GetType("System.Threading.Tasks.VoidTaskResult");

    public static object? Of(Task completed) {
        for (var type = completed.GetType(); type != null; type = type.BaseType) {
            if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(Task<>)) {
                continue;
            }

            if (type.GetGenericArguments()[0] == VoidResult) {
                return null;
            }

            return Results
                .GetOrAdd(type, task => task.GetProperty(nameof(Task<int>.Result)))
                ?.GetValue(completed);
        }

        return null;
    }
}
