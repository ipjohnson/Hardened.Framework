using System.Reflection;
using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Shared.Runtime.Collections;
using Hardened.SourceGeneration.Testing;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Handlers;
using Microsoft.CodeAnalysis;
using NSubstitute;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests.Routing;

/// <summary>
/// Runs the web generator over a source snippet, compiles what it emitted, loads the result and
/// then actually calls the routing table it produced.
///
/// <para>
/// Asserting on the emitted route-matching source proves the generator wrote the characters
/// expected of it. It does not prove a request reaches the right handler — the route tree is a
/// hand-written span comparison per path segment, and an off-by-one in it produces C# that
/// compiles perfectly and routes to the wrong place. So every routing test here compiles the
/// output (<see cref="GeneratorResult.AssertNoErrors"/>) and then drives it.
/// </para>
/// </summary>
internal sealed class GeneratedRoutingTable {

    /// <summary>
    /// The assemblies a generated web application binds against. <c>typeof</c> rather than a name
    /// so the assembly is loaded by the time references are collected.
    /// </summary>
    internal static readonly Type[] Anchors = [
        typeof(GetAttribute),               // Hardened.Web.Runtime
        typeof(FromBodyAttribute)           // Hardened.Requests.Abstract
    ];

    /// <summary>
    /// A distinct assembly name per compilation. Loading two assemblies with the same simple name
    /// into the default context succeeds but makes which one a name resolves to depend on load
    /// order, which is exactly the kind of test-order coupling that is impossible to debug later.
    /// </summary>
    private static int _assemblyCounter;

    private readonly object _routingTable;
    private readonly MethodInfo _getExecutionRequestHandler;

    private GeneratedRoutingTable(
        GeneratorResult result,
        object routingTable,
        MethodInfo method) {
        Result = result;
        _routingTable = routingTable;
        _getExecutionRequestHandler = method;
    }

    /// <summary>Everything the generator emitted, for tests that also assert on the source.</summary>
    public GeneratorResult Result { get; }

    /// <summary>
    /// Generates, compiles, loads and constructs the routing table for <paramref name="source"/>.
    /// The source must declare a <c>[HardenedModule]</c> partial class — that is what the generator
    /// treats as the application entry point, and without one it emits handlers but no route table.
    /// </summary>
    public static GeneratedRoutingTable For(string source, string entryPointType = "TestApp.TestApplication") {
        var assemblyName = "WebRoutingTest" + Interlocked.Increment(ref _assemblyCounter);

        var result = GeneratorTestHarness.Run(
            new Dictionary<string, string> { ["Test.cs"] = source },
            new IIncrementalGenerator[] { new WebLibrarySourceGenerator() },
            Anchors,
            assemblyName: assemblyName);

        result.AssertNoErrors();

        using var stream = new MemoryStream();

        var emitResult = result.Compilation.Emit(stream);

        Assert.True(emitResult.Success,
            "The generated code compiled but could not be emitted:" + Environment.NewLine +
            string.Join(Environment.NewLine, emitResult.Diagnostics
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));

        var assembly = Assembly.Load(stream.ToArray());

        var applicationType = assembly.GetType(entryPointType);

        Assert.True(applicationType != null,
            $"The compiled test assembly has no type '{entryPointType}'. " +
            $"Types: {string.Join(", ", assembly.GetTypes().Select(type => type.FullName))}");

        // The routing table is a private nested class, so a consumer never names it. Reflection is
        // the only way in, and is also how the DI registration the generator emits reaches it.
        var routingTableType = applicationType!.GetNestedType("RoutingTable",
            BindingFlags.Public | BindingFlags.NonPublic);

        Assert.True(routingTableType != null,
            $"'{entryPointType}' has no nested RoutingTable — the generator emitted handlers but no " +
            "route table. That happens when the source has no [HardenedModule] entry point. " +
            $"Generated: {string.Join(", ", result.GeneratedSources.Keys)}");

        var instance = Activator.CreateInstance(
            routingTableType!,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [HandlerServiceProvider()],
            culture: null);

        var method = routingTableType!.GetMethod(
            "GetExecutionRequestHandler",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.True(method != null, "The generated RoutingTable has no GetExecutionRequestHandler method.");

        return new GeneratedRoutingTable(result, instance!, method!);
    }

    /// <summary>
    /// Routes one request. Returns null when nothing matched, which is what the web handler service
    /// treats as "fall through to static content, then 404".
    /// </summary>
    public RequestHandlerInfo? Route(string method, string path) {
        var context = Substitute.For<IExecutionContext>();
        var request = Substitute.For<IExecutionRequest>();

        request.Method.Returns(method);
        request.Path.Returns(path);
        context.Request.Returns(request);

        return (RequestHandlerInfo?)_getExecutionRequestHandler.Invoke(_routingTable, [context]);
    }

    /// <summary>Routes a request and fails the test if nothing matched.</summary>
    public IExecutionRequestHandlerInfo Handler(string method, string path) {
        var handler = Route(method, path);

        Assert.True(handler != null, $"{method} {path} did not match any route.");

        return handler!.Handler.HandlerInfo;
    }

    /// <summary>The path token values bound by the route that matched, keyed by token name.</summary>
    public IReadOnlyDictionary<string, string?> PathTokens(string method, string path) {
        var handler = Route(method, path);

        Assert.True(handler != null, $"{method} {path} did not match any route.");

        var tokens = new Dictionary<string, string?>();

        for (var i = 0; i < handler!.PathTokens.Count; i++) {
            var token = handler.PathTokens.Get(i);

            tokens[token.TokenName] = token.TokenValue;
        }

        return tokens;
    }

    /// <summary>
    /// The minimum a generated handler's constructor needs. Constructing one resolves the IO,
    /// instance and global filter services, so routing to a handler for the first time fails
    /// without them even though nothing here executes a request.
    /// </summary>
    private static IServiceProvider HandlerServiceProvider() {
        var globalFilters = Substitute.For<IGlobalFilterRegistry>();

        // Returns a fresh mutable list per call: ExecutionHelper adds to what it is handed.
        globalFilters.GetFilters(Arg.Any<IExecutionRequestHandlerInfo>())
            .Returns(_ => new List<RequestFilterInfo>());

        var serviceProvider = Substitute.For<IServiceProvider>();

        serviceProvider.GetService(typeof(IGlobalFilterRegistry)).Returns(globalFilters);
        serviceProvider.GetService(typeof(IIOFilterProvider)).Returns(Substitute.For<IIOFilterProvider>());
        serviceProvider.GetService(typeof(IInstanceFilterProvider))
            .Returns(Substitute.For<IInstanceFilterProvider>());
        serviceProvider.GetService(typeof(IStringBuilderPool)).Returns(Substitute.For<IStringBuilderPool>());

        return serviceProvider;
    }
}
