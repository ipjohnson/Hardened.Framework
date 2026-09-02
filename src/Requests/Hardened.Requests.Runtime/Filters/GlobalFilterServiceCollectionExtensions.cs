using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Requests.Runtime.Filters;

/// <summary>
/// Applying a filter provider to every handler in the application.
/// </summary>
public static class GlobalFilterServiceCollectionExtensions {

    /// <summary>
    /// Asks <paramref name="provider"/> about every handler <paramref name="when"/> admits.
    ///
    /// <code>
    /// services.AddGlobalFilter(
    ///     new CacheResponseAttribute&lt;VaryByRoute&gt; { Duration = 60 },
    ///     when: info =&gt; info.Method == "GET");
    /// </code>
    /// </summary>
    /// <param name="when">
    /// Which handlers this covers, or null for all of them. Read once per handler, as its filter
    /// chain is built.
    /// </param>
    /// <remarks>
    /// <para>
    /// On <see cref="IServiceCollection"/> rather than <see cref="IGlobalFilterRegistry"/>, because
    /// the registry's own route takes a function returning one nullable filter and a provider
    /// yielding two would lose the second.
    /// <see cref="GlobalFilterRegistry"/> already takes every registered
    /// <see cref="IRequestFilterProvider"/> in its constructor, so this needs no new plumbing on the
    /// registry side.
    /// </para>
    /// <para>
    /// <b>Nothing in the shipping framework registered an <c>IRequestFilterProvider</c> as a
    /// service before this.</b> The registry's injected list was empty in every application, and
    /// every global filter arrived through <c>RegisterFilter</c> instead. The mechanism worked by
    /// construction and had nothing behind it; <c>GlobalFilterRegistryTests</c> covers it now.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddGlobalFilter(
        this IServiceCollection services,
        IRequestFilterProvider provider,
        Func<IExecutionRequestHandlerInfo, bool>? when = null) {
        services.AddSingleton(
            when == null ? provider : new ConditionalFilterProvider(provider, when));

        return services;
    }
}
