using System.Runtime.CompilerServices;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;

namespace Hardened.Requests.Runtime.Execution;

/// <summary>
/// What the composed-chain log calls a filter.
/// </summary>
internal static class FilterNames {

    /// <summary>
    /// The type's name without the arity a generic carries: <c>ValidationFilter</c> rather than
    /// <c>ValidationFilter`1</c>.
    /// </summary>
    public static string Of(Type type) {
        var name = type.Name;
        var arity = name.IndexOf('`');

        return arity < 0 ? name : name.Substring(0, arity);
    }

    public static string Of(IExecutionFilter filter) => Of(filter.GetType());

    /// <summary>
    /// The name the registration gave, or failing that the type that made it: a lambda's
    /// enclosing type, found by walking out of what the compiler generated for it, and the factory
    /// method's own name when there is no type at all.
    /// </summary>
    /// <remarks>
    /// Reflection, and deliberately only here. This runs once per handler, and only when the log
    /// is enabled, so nothing on a request pays for it.
    /// </remarks>
    public static string Of(RequestFilterInfo filter) {
        if (filter.Name != null) {
            return filter.Name;
        }

        var method = filter.FilterFunc.Method;
        var type = method.DeclaringType;

        while (type != null && type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)) {
            type = type.DeclaringType;
        }

        return type == null ? method.Name : Of(type);
    }
}
