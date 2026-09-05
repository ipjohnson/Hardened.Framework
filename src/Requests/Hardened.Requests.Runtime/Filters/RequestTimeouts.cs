using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Requests.Abstract.Timeouts;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Requests.Runtime.Filters;

/// <summary>
/// The budget for every operation in the application that no nearer declaration bounds.
///
/// <code>
/// [HardenedModule]
/// [Enable&lt;RequestTimeouts&gt;]      // the default budget, 30 seconds
/// [KestrelRuntime]
/// public partial class Application { }
///
/// [HardenedModule]
/// [RequestTimeouts(5000)]           // the budget written
/// [KestrelRuntime]
/// public partial class Application { }
/// </code>
///
/// <para>
/// <b>Two spellings, one module.</b> <c>[Enable&lt;T&gt;]</c> is one attribute name shared by every
/// optional feature and takes no arguments - the generator turns it into
/// <c>AddModule(new RequestTimeouts())</c> - so the number cannot ride on it. The attribute
/// DependencyModules generates from this module's own constructor is where the number goes.
/// Writing both registers two defaults and the tighter applies, which is a defined answer rather
/// than a hazard, but say it once.
/// </para>
/// <para>
/// <b>The outermost rung.</b> An operation, its class and the handler's own assembly all beat this;
/// an <c>IRequestTimeoutConvention</c> can tighten it. It is the fallback for handlers nothing else
/// spoke about. An application wanting a non-504 default writes
/// <c>[assembly: Timeout(Milliseconds = 5000, Status = 503)]</c> instead, which carries the status
/// as well and, being the handler's own assembly, is the more specific statement anyway.
/// </para>
/// <para>
/// <b>Opt in.</b> Nothing is bounded until this is written or something declares <c>[Timeout]</c>.
/// A linked <c>CancellationTokenSource</c> and a timer per request is a per-request cost, and an
/// application that declares nothing pays nothing is the rule <c>HardenedRequestModule</c> states.
/// </para>
/// </summary>
/// <remarks>
/// The name is not <c>RequestTimeout</c>, which is taken by the 408 response record in
/// <c>Hardened.Requests.Abstract.Responses</c>. <c>RequestTimeouts</c> matches ASP.NET's
/// <c>AddRequestTimeouts</c> and collides with nothing.
/// </remarks>
[DependencyModule]
public partial class RequestTimeouts : IServiceCollectionConfiguration {

    /// <summary>
    /// The default budget. What <c>[Enable&lt;RequestTimeouts&gt;]</c> installs.
    /// </summary>
    public RequestTimeouts() : this(TimeoutPolicy.DefaultMilliseconds) { }

    /// <summary>
    /// The budget written at the entry point, as <c>[RequestTimeouts(5000)]</c>.
    /// </summary>
    public RequestTimeouts(int milliseconds) {
        Milliseconds = milliseconds;
    }

    /// <summary>
    /// How long an operation that nothing else bounds may take.
    /// </summary>
    /// <remarks>
    /// Read-only, and that is load-bearing rather than tidiness. DependencyModules turns every
    /// settable property into a property on the generated attribute defaulting to
    /// <c>default(T)</c>, and copies it onto the module guarded by a null check - a guard a value
    /// type always passes. A settable <c>int</c> here would therefore be assigned 0 by
    /// <c>[RequestTimeouts(5000)]</c>, overwriting the constructor argument that was just written.
    /// A constructor parameter has no such problem: it is required at the attribute's call site.
    /// The same reason <c>HardenedOpenApiUi</c> and <c>HardenedStaticContent</c> make every
    /// property of theirs nullable.
    /// </remarks>
    public int Milliseconds { get; }

    /// <summary>
    /// Registers the policy itself, which is what <c>TimeoutResolver</c> reads as the outermost
    /// rung of the cascade. No global filter: the chain builder installs one filter per handler
    /// from whatever the cascade resolved, so there is nothing here to stand down for a handler
    /// that declared its own.
    /// </summary>
    public void ConfigureServices(IServiceCollection services) {
        services.AddSingleton(new TimeoutPolicy(Milliseconds));
    }

    /// <summary>
    /// Two installs of the same budget are one install, so a library module and the entry point
    /// both asking for five seconds register one default.
    /// </summary>
    /// <remarks>
    /// By value rather than by type, which is where this differs from <c>ConditionalGet</c> and
    /// <c>ResponseCompression</c>: every install of those is the same install, and every install of
    /// this carries a number.
    /// </remarks>
    public override bool Equals(object? obj) =>
        obj is RequestTimeouts other && other.Milliseconds == Milliseconds;

    public override int GetHashCode() => Milliseconds;
}
