using System.Collections.Concurrent;
using System.Reflection;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Timeouts;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Requests.Runtime.Filters;

/// <summary>
/// Which deadline applies to a handler, from the four places one can be declared.
/// </summary>
/// <remarks>
/// <para>
/// <b>Most specific wins.</b> The operation, then its class, then the handler's own assembly, then
/// the entry point's default. Nothing is combined: unlike a requirement, two budgets do not compose
/// into a third, so the nearest declaration to the handler is the answer and the rest are
/// fallbacks.
/// </para>
/// <para>
/// <b>The assembly beats the entry point, and that is the decision the order turns on.</b> A
/// <c>[WebLibrary]</c> project writing <c>[assembly: Timeout(Milliseconds = 2000)]</c> is saying
/// something specific about its own handlers; an entry point writing
/// <c>[Enable&lt;RequestTimeouts&gt;]</c> is stating a blanket fallback for handlers that said
/// nothing. Read the other way round, a host would silently loosen a bound a library deliberately
/// set. The consequence worth knowing is that the assembly rung is the <em>handler's</em> assembly,
/// so <c>[assembly: Timeout]</c> written beside an entry point covers that assembly's own handlers
/// and not a referenced library's.
/// </para>
/// <para>
/// Conventions run last and can only tighten - see <see cref="IRequestTimeoutConvention"/>.
/// </para>
/// <para>
/// Asked once per handler, as its filter chain is built, so the reflection over an assembly's
/// attributes happens once per assembly and the rest is a walk over metadata already in memory.
/// </para>
/// </remarks>
internal static class TimeoutResolver {

    /// <summary>
    /// One lookup per assembly rather than one per handler, since a controller with twenty routes
    /// asks the same question twenty times.
    /// </summary>
    private static readonly ConcurrentDictionary<Assembly, TimeoutPolicy?> AssemblyPolicies = new();

    public static TimeoutPolicy? Resolve(
        IServiceProvider serviceProvider, IExecutionRequestHandlerInfo handlerInfo) {
        // The operation and its class, in that order: the generator emits a method's own attributes
        // ahead of its class's, and IExecutionRequestHandlerInfo.TimeoutFrom takes the first.
        var resolved = Checked(handlerInfo.Timeout, handlerInfo, "on the operation or its class");

        resolved ??= Checked(
            ForAssembly(handlerInfo.HandlerType.Assembly),
            handlerInfo,
            "on the assembly " + handlerInfo.HandlerType.Assembly.GetName().Name);

        resolved ??= Checked(
            EntryPointDefault(serviceProvider), handlerInfo, "by the application's default");

        return Tightened(serviceProvider, handlerInfo, resolved);
    }

    /// <summary>
    /// The declaration on an assembly, or null. Cached, and the cache is keyed on the assembly
    /// rather than the handler because that is what the answer depends on.
    /// </summary>
    private static TimeoutPolicy? ForAssembly(Assembly assembly) =>
        AssemblyPolicies.GetOrAdd(
            assembly,
            static declaring => declaring.GetCustomAttributes()
                .OfType<IDeclaresTimeout>()
                .FirstOrDefault()
                ?.Timeout);

    /// <summary>
    /// The tightest budget any entry-point module registered, or null.
    /// </summary>
    /// <remarks>
    /// Tightest rather than last, because both spellings of the module reach the container:
    /// <c>[Enable&lt;RequestTimeouts&gt;]</c> and <c>[RequestTimeouts(5000)]</c> are separate
    /// <c>LoadModules</c> passes, so module equality cannot collapse them and an application that
    /// writes both registers two. Taking the tighter makes that a defined answer rather than
    /// whichever the container happened to return last, and it is the same rule the conventions
    /// follow.
    /// </remarks>
    private static TimeoutPolicy? EntryPointDefault(IServiceProvider serviceProvider) {
        // GetService rather than GetServices, for the reason ExecutionHelper.ApplyConventions
        // gives: the convenience overload resolves IEnumerable<T> as required, and Hardened's
        // container does not synthesise an empty one.
        var registered = serviceProvider.GetService<IEnumerable<TimeoutPolicy>>();

        if (registered == null) {
            return null;
        }

        TimeoutPolicy? tightest = null;

        foreach (var policy in registered) {
            tightest = TimeoutPolicy.Tighter(tightest, policy);
        }

        return tightest;
    }

    /// <summary>
    /// Folds in whatever the conventions would put on this handler, taking the tighter each time.
    /// </summary>
    private static TimeoutPolicy? Tightened(
        IServiceProvider serviceProvider,
        IExecutionRequestHandlerInfo handlerInfo,
        TimeoutPolicy? resolved) {
        var conventions = serviceProvider.GetService<IEnumerable<IRequestTimeoutConvention>>();

        if (conventions == null) {
            return resolved;
        }

        foreach (var convention in conventions) {
            resolved = TimeoutPolicy.Tighter(
                resolved,
                Checked(
                    convention.Apply(handlerInfo),
                    handlerInfo,
                    "by " + convention.GetType().Name));
        }

        return resolved;
    }

    /// <summary>
    /// The policy, or a failure naming the handler and where the number came from.
    /// </summary>
    /// <remarks>
    /// Checked as the chain is composed, which is once per handler at startup. A zero would
    /// otherwise refuse every request the moment it was deployed, and a negative one throws from
    /// <c>CancelAfter</c> on the first request - both of them a long way from the declaration that
    /// caused it.
    /// </remarks>
    private static TimeoutPolicy? Checked(
        TimeoutPolicy? policy, IExecutionRequestHandlerInfo handlerInfo, string source) {
        if (policy is { Milliseconds: <= 0 }) {
            throw new InvalidOperationException(
                $"The timeout declared {source} for {handlerInfo.Method} {handlerInfo.Path} is " +
                $"{policy.Milliseconds} milliseconds. A budget has to be greater than zero; a " +
                "handler that should not be bounded declares no timeout instead.");
        }

        return policy;
    }
}
