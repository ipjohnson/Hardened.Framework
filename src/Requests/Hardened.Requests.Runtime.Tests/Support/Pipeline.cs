using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Logging;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.QueryString;
using Hardened.Requests.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Hardened.Requests.Runtime.Tests.Support;

/// <summary>
/// Real request, real response, real context, real service provider.
///
/// The execution pipeline is a chain of objects that mutate a shared context; a fully mocked
/// context lets a test agree with itself while the real wiring is broken. Everything here is
/// the production type wherever a production type exists, and a substitute only where the
/// collaborator is genuinely outside the pipeline.
/// </summary>
public static class Pipeline {

    /// <summary>
    /// A context whose request and response are the in-memory transport implementations, with
    /// a service provider carrying the services the pipeline resolves at run time.
    /// </summary>
    /// <remarks>
    /// Deliberately takes no <c>CancellationToken</c>. Adding one with a default value made
    /// xUnit1051 fire at every one of the two hundred call sites in this project, because the rule
    /// reports a token parameter left at its default rather than set to
    /// <c>TestContext.Current.CancellationToken</c>. A test that needs a cancellable context builds
    /// one directly - see <see cref="Cancellable"/>.
    /// </remarks>
    public static IExecutionContext Context(
        string method = "GET",
        string path = "/",
        string? accept = "application/json",
        byte[]? body = null,
        Action<ServiceCollection>? configureServices = null) {
        return Build(method, path, accept, body, configureServices, CancellationToken.None);
    }

    /// <summary>
    /// A context whose <c>CancellationToken</c> is one the test controls.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Context"/> so that the token is always passed explicitly at the one
    /// or two places that want it, rather than defaulted at every place that does not.
    /// </remarks>
    public static IExecutionContext Cancellable(
        CancellationToken cancellationToken,
        string method = "GET",
        string path = "/",
        byte[]? body = null,
        Action<ServiceCollection>? configureServices = null) {
        return Build(method, path, "application/json", body, configureServices, cancellationToken);
    }

    private static IExecutionContext Build(
        string method,
        string path,
        string? accept,
        byte[]? body,
        Action<ServiceCollection>? configureServices,
        CancellationToken cancellationToken) {

        var services = new ServiceCollection();

        services.AddSingleton<IRequestLogger>(Substitute.For<IRequestLogger>());

        configureServices?.Invoke(services);

        var provider = services.BuildServiceProvider();

        var request = new TestExecutionRequest(
            method, path, accept, new SimpleQueryStringCollection(new Dictionary<string, string>())) {
            Body = body is null ? Stream.Null : new MemoryStream(body)
        };

        return new TestExecutionContext(
            provider,
            provider,
            Substitute.For<IKnownServices>(),
            request,
            new TestExecutionResponse(new MemoryStream()),
            cancellationToken);
    }

    /// <summary>
    /// Builds a chain over the supplied filters, in the order given.
    /// </summary>
    public static ExecutionChain Chain(IExecutionContext context, params IExecutionFilter[] filters) =>
        new(filters.Select<IExecutionFilter, Func<IExecutionContext, IExecutionFilter>>(
            filter => _ => filter).ToList(), context);

    /// <summary>
    /// Records that it ran, in a shared log, then continues down the chain.
    /// </summary>
    public sealed class Recording : IExecutionFilter {
        private readonly List<string> _log;

        public Recording(List<string> log, string name) {
            _log = log;
            Name = name;
        }

        public string Name { get; }

        public Task Execute(IExecutionChain chain) {
            _log.Add(Name);

            return chain.Next();
        }
    }

    /// <summary>
    /// Records that it ran and then stops, never calling <c>Next</c>. Everything ordered after
    /// it must not run.
    /// </summary>
    public sealed class ShortCircuiting : IExecutionFilter {
        private readonly List<string> _log;
        private readonly string _name;

        public ShortCircuiting(List<string> log, string name) {
            _log = log;
            _name = name;
        }

        public Task Execute(IExecutionChain chain) {
            _log.Add(_name);

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Runs the supplied delegate in place of a filter body, then continues.
    /// </summary>
    public sealed class Inline : IExecutionFilter {
        private readonly Func<IExecutionChain, Task> _body;

        public Inline(Func<IExecutionChain, Task> body) {
            _body = body;
        }

        public Task Execute(IExecutionChain chain) => _body(chain);
    }

    public static NullLogger<T> Logger<T>() => NullLogger<T>.Instance;
}
