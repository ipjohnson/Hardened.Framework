using System.Reflection;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.Filters;
using Hardened.Requests.Runtime.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Execution;

/// <summary>
/// A filter declared on the entry point, as every handler's chain is built.
/// </summary>
/// <remarks>
/// <para>
/// The generator emits the declarations once beside the routing table and registers them as
/// <see cref="IApplicationFilterDeclarations"/>. What happens next is here: each is dropped for a
/// handler that declares the same type nearer, and the rest are merged into that handler's metadata
/// and asked for their filters.
/// </para>
/// <para>
/// Driven through <c>ExecutionHelper</c> and run, as <c>FilterOrderingTests</c> is, because the
/// merge and the installation are two halves of one answer and asserting the merged list alone
/// would not show that anything ran.
/// </para>
/// </remarks>
public class ApplicationFilterRungTests {

    private const string Instance = "instance";
    private const string Io = "io";
    private const string Invoke = "invoke";

    private class Controller { }

    /// <summary>
    /// A filter attribute of the shape an application declares on its module class: it decides for
    /// itself which handlers it applies to, and installs nothing on the rest.
    /// </summary>
    private sealed class RecordingAttribute : Attribute, IRequestFilterProvider {
        private readonly List<string> _log;
        private readonly string _name;
        private readonly int? _order;
        private readonly string? _method;

        public RecordingAttribute(
            List<string> log, string name, int? order = null, string? method = null) {
            _log = log;
            _name = name;
            _order = order;
            _method = method;
        }

        public IEnumerable<RequestFilterInfo> GetFilters(IExecutionRequestHandlerInfo handlerInfo) {
            if (_method != null && !string.Equals(handlerInfo.Method, _method, StringComparison.Ordinal)) {
                yield break;
            }

            yield return new RequestFilterInfo(_ => new Pipeline.Recording(_log, _name), _order, _name);
        }
    }

    /// <summary>A second type, so a declaration can be suppressed by one and not by the other.</summary>
    private sealed class OtherRecordingAttribute : Attribute, IRequestFilterProvider {
        private readonly List<string> _log;

        public OtherRecordingAttribute(List<string> log) {
            _log = log;
        }

        public IEnumerable<RequestFilterInfo> GetFilters(IExecutionRequestHandlerInfo handlerInfo) {
            yield return new RequestFilterInfo(
                _ => new Pipeline.Recording(_log, "other"), FilterOrder.Before, "other");
        }
    }

    private sealed class Declarations : IApplicationFilterDeclarations {
        public Declarations(params object[] declared) {
            Declared = declared;
        }

        public IReadOnlyList<object> Declared { get; }
    }

    /// <summary>Declarations written in a compilation the handler does not belong to.</summary>
    private sealed class ForeignDeclarations : IApplicationFilterDeclarations {
        public ForeignDeclarations(params object[] declared) {
            Declared = declared;
        }

        public IReadOnlyList<object> Declared { get; }

        public Assembly DeclaringAssembly => typeof(string).Assembly;
    }

    /// <summary>
    /// Composes and runs a handler's chain the way the generated code does, returning the names of
    /// the filters that executed in the order they ran.
    /// </summary>
    private static async Task<(List<string> Ran, IExecutionRequestHandlerInfo Composed)> Run(
        List<string> log,
        IApplicationFilterDeclarations[]? declarations,
        object[]? metadata = null,
        string method = "GET",
        IRequestFilterProvider[]? handlerProviders = null) {
        var ioProvider = Substitute.For<IIOFilterProvider>();

        ioProvider.ProvideFilter(
                Arg.Any<IExecutionRequestHandlerInfo>(),
                Arg.Any<Func<IExecutionContext, Task<IExecutionRequestParameters>>>())
            .Returns(new Pipeline.Recording(log, Io));

        var instanceProvider = Substitute.For<IInstanceFilterProvider>();

        instanceProvider.ProvideFilter<Controller>(Arg.Any<IServiceProvider>())
            .Returns(new Pipeline.Recording(log, Instance));

        var context = Pipeline.Context(method: method, configureServices: services => {
            services.AddSingleton<IGlobalFilterRegistry>(
                new GlobalFilterRegistry(Array.Empty<IRequestFilterProvider>()));
            services.AddSingleton(ioProvider);
            services.AddSingleton(instanceProvider);

            foreach (var module in declarations ?? Array.Empty<IApplicationFilterDeclarations>()) {
                services.AddSingleton(module);
            }
        });

        context.HandlerInstance = new Controller();

        var handlerInfo = new ExecutionRequestHandlerInfo(
            "/books", method, typeof(Controller), nameof(Run), metadata: metadata);

        var setup = ExecutionHelper.StandardFilterEmptyParameters<Controller>(
            context.RequestServices,
            handlerInfo,
            (_, _) => log.Add(Invoke),
            handlerProviders ?? Array.Empty<IRequestFilterProvider>());

        await new ExecutionChain(setup.Filters, context).Next();

        return (log, setup.HandlerInfo);
    }

    /// <summary>
    /// The declaration installs on a handler that carries none of its own, which is the whole
    /// point: one attribute on the module class covers the application.
    /// </summary>
    [Fact]
    public async Task ADeclarationReachesAHandlerThatCarriesNone() {
        var log = new List<string>();

        var (ran, _) = await Run(log, [new Declarations(new RecordingAttribute(log, "wide"))]);

        Assert.Contains("wide", ran);
    }

    /// <summary>
    /// And is merged into the handler's own metadata, which is what lets a filter ask what else was
    /// declared on the handler it is being installed on - the question
    /// <c>ConditionalGetAttribute.Declares</c> asks.
    /// </summary>
    [Fact]
    public async Task ADeclarationThatReachesAHandlerIsInItsMetadata() {
        var log = new List<string>();
        var declared = new RecordingAttribute(log, "wide");

        var (_, composed) = await Run(log, [new Declarations(declared)]);

        Assert.Contains(declared, composed.Metadata);
    }

    /// <summary>
    /// Nearest wins. A handler declaring the same type keeps its own and the wider one is dropped,
    /// so the filter is installed once rather than twice.
    /// </summary>
    [Fact]
    public async Task AHandlerDeclaringTheSameTypeSuppressesTheWiderOne() {
        var log = new List<string>();
        var own = new RecordingAttribute(log, "own");
        var wide = new RecordingAttribute(log, "wide");

        var (ran, composed) = await Run(
            log, [new Declarations(wide)], metadata: [own], handlerProviders: [own]);

        Assert.Contains("own", ran);
        Assert.DoesNotContain("wide", ran);
        Assert.DoesNotContain(wide, composed.Metadata);
    }

    /// <summary>
    /// Keyed on the type rather than on the fact that something was declared, so an unrelated
    /// declaration on the handler suppresses nothing.
    /// </summary>
    [Fact]
    public async Task ADeclarationOfAnotherTypeSuppressesNothing() {
        var log = new List<string>();
        var other = new OtherRecordingAttribute(log);
        var wide = new RecordingAttribute(log, "wide");

        var (ran, _) = await Run(
            log, [new Declarations(wide)], metadata: [other], handlerProviders: [other]);

        Assert.Contains("wide", ran);
        Assert.Contains("other", ran);
    }

    /// <summary>
    /// A declaration decides for itself which handlers it applies to, and installs nothing on the
    /// rest. <c>[ConditionalGet]</c> on a module class is this: it covers the reads and leaves the
    /// writes alone.
    /// </summary>
    [Fact]
    public async Task ADeclarationInstallsNothingOnAHandlerItDoesNotApplyTo() {
        var log = new List<string>();

        var (ran, _) = await Run(
            log,
            [new Declarations(new RecordingAttribute(log, "wide", method: "GET"))],
            method: "POST");

        Assert.DoesNotContain("wide", ran);
    }

    /// <summary>
    /// Ties break on insertion order, and a declaration covering the application is inserted with
    /// the registry's - ahead of a handler's own asking for the same position. The wider
    /// declaration was written first.
    /// </summary>
    [Fact]
    public async Task AWiderDeclarationRunsAheadOfTheHandlersOwnAtTheSameOrder() {
        var log = new List<string>();
        var own = new RecordingAttribute(log, "own", FilterOrder.Before);
        var wide = new OtherRecordingAttribute(log);

        var (ran, _) = await Run(log, [new Declarations(wide)], handlerProviders: [own]);

        Assert.Equal(["other", "own"], ran.Where(name => name is "other" or "own").ToArray());
    }

    /// <summary>
    /// Every module in the handler's own compilation contributes, rather than whichever registered
    /// last. A process composes as many modules as it references.
    /// </summary>
    [Fact]
    public async Task EveryModuleThatDeclaresSomethingContributes() {
        var log = new List<string>();

        var (ran, _) = await Run(log, [
            new Declarations(new RecordingAttribute(log, "first")),
            new Declarations(new OtherRecordingAttribute(log))
        ]);

        Assert.Contains("first", ran);
        Assert.Contains("other", ran);
    }

    /// <summary>
    /// A module declares filters for the handlers it was compiled with and no others. Compile-time
    /// collection cannot cross that seam, and a referenced library's handlers were described in a
    /// document written before this application existed.
    /// </summary>
    [Fact]
    public async Task DeclarationsFromAnotherCompilationReachNothing() {
        var log = new List<string>();

        var (ran, composed) =
            await Run(log, [new ForeignDeclarations(new RecordingAttribute(log, "wide"))]);

        Assert.DoesNotContain("wide", ran);
        Assert.Empty(composed.Metadata);
    }

    /// <summary>
    /// An application whose entry point declares no filter registers nothing, and composes exactly
    /// the chain it composed before this existed.
    /// </summary>
    [Fact]
    public async Task NothingRegisteredChangesNothing() {
        var (ran, composed) = await Run(new List<string>(), declarations: null);

        Assert.Equal([Instance, Io, Invoke], ran);
        Assert.Empty(composed.Metadata);
    }
}
