using System.Collections.Concurrent;
using System.Reflection;
using DependencyModules.Testing.Attributes.Interfaces;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hardened.Web.Testing;

/// <summary>
/// The three construction routes for a typed client, and the <see cref="HttpClient"/> they run over.
/// </summary>
/// <remarks>
/// <para>
/// A public <see cref="ITestClientFactory{TClient}"/> for the type in the test assembly, discovered
/// once per assembly and cached; then an <see cref="ITestClientRoute"/> the assembly named in a
/// <see cref="TestClientRouteAttribute"/>, which answers for a whole generator's output rather than
/// for one client; otherwise a single public constructor taking exactly one
/// <see cref="HttpClient"/>. None of the three is a test failure naming all of them, because a
/// client that reaches the resolver's own fallback fails inside <c>ActivatorUtilities</c> naming a
/// constructor parameter a reader has never heard of.
/// </para>
/// <para>
/// The factory wins over the route, so a test project that wants one client built its own way says
/// so without giving up the route for the rest.
/// </para>
/// <para>
/// The plain <see cref="HttpClient"/> is deliberate. Wrapping the handler in a generator's own
/// factory would put its retry and redirect middleware in front of the pipeline, and a test of a
/// 429 or a 308 wants to see what the pipeline answered, not what the middleware made of it.
/// </para>
/// </remarks>
internal static class TestClientBuilder {

    /// <summary>The base address a client resolves relative URLs against; the handler ignores it.</summary>
    public static readonly Uri BaseAddress = new("http://harness/");

    private static readonly ConcurrentDictionary<Assembly, IReadOnlyDictionary<Type, Type>> Factories = new();
    private static readonly ConcurrentDictionary<Assembly, IReadOnlyList<ITestClientRoute>> Routes = new();
    private static readonly ConcurrentDictionary<Assembly, IReadOnlyList<ITestClientReader>> Readers = new();

    public static HttpClient CreateHttpClient(IServiceProvider rootServiceProvider, TestCredential? credential) {
        var client = new HttpClient(new PipelineHttpMessageHandler(rootServiceProvider, credential)) {
            BaseAddress = BaseAddress
        };

        credential?.ApplyTo(client);

        return client;
    }

    /// <summary>Whether any of the three routes applies to <paramref name="clientType"/>.</summary>
    public static bool HasRoute(Type clientType, Assembly testAssembly) =>
        FactoryFor(clientType, testAssembly) != null ||
        RouteFor(clientType, testAssembly) != null ||
        HttpClientConstructor(clientType) != null;

    public static object Build(Type clientType, TestClientContext context, Assembly testAssembly) {
        var factoryType = FactoryFor(clientType, testAssembly);

        if (factoryType != null) {
            var factory = Activator.CreateInstance(factoryType)!;
            var create = typeof(ITestClientFactory<>).MakeGenericType(clientType).GetMethod("Create")!;

            return create.Invoke(factory, new object[] { context.Http })!;
        }

        if (RouteFor(clientType, testAssembly) is { } route) {
            return route.Build(context, clientType);
        }

        var constructor = HttpClientConstructor(clientType);

        if (constructor != null) {
            return constructor.Invoke(new object[] { context.Http });
        }

        throw new InvalidOperationException(NoRouteMessage(clientType, testAssembly));
    }

    public static TestClientContext CreateContext(IServiceProvider rootServiceProvider, TestCredential? credential) =>
        new(rootServiceProvider, credential, CreateHttpClient(rootServiceProvider, credential));

    public static string NoRouteMessage(Type clientType, Assembly testAssembly) =>
        $"{clientType.FullName} cannot be built for a test parameter. None of the three routes " +
        $"applies: {testAssembly.GetName().Name} declares no public ITestClientFactory<{clientType.Name}>, " +
        $"{RouteNote(testAssembly)}, and {clientType.Name} has no single public constructor taking " +
        $"exactly one HttpClient. Add a factory - one class, one method from HttpClient to " +
        $"{clientType.Name} - or reference the client generator's testing package and name its route " +
        "in an assembly attribute.";

    private static string RouteNote(Assembly testAssembly) {
        var routes = RoutesFor(testAssembly);

        return routes.Count == 0
            ? "it names no ITestClientRoute in a [assembly: TestClientRoute] attribute"
            : "no route it names recognises the type (" +
              string.Join(", ", routes.Select(route => route.GetType().Name)) + ")";
    }

    /// <summary>
    /// The value for a parameter carrying a credential attribute: the harness or a client, built
    /// with that parameter's own credential, or null to stand aside for ordinary resolution.
    /// </summary>
    public static object? ForParameter(ITestMethodContext testMethod, IServiceProvider serviceProvider, ParameterInfo parameter) {
        var credential = TestCredential.Resolve(testMethod, parameter);
        var testAssembly = testMethod.Method.DeclaringType!.Assembly;
        var root = serviceProvider.GetRequiredService<IApplicationRoot>().Provider;

        if (parameter.ParameterType == typeof(ITestWebApp)) {
            var loggerType = typeof(ILogger<>).MakeGenericType(testMethod.Method.DeclaringType!);

            return new TestWebApp(
                serviceProvider.GetRequiredService<IApplicationRoot>(),
                (ILogger)serviceProvider.GetRequiredService(loggerType),
                credential,
                testAssembly);
        }

        if (parameter.ParameterType == typeof(HttpClient)) {
            return CreateHttpClient(root, credential);
        }

        if (!HasRoute(parameter.ParameterType, testAssembly)) {
            return null;
        }

        return Build(parameter.ParameterType, CreateContext(root, credential), testAssembly);
    }

    /// <summary>
    /// Whether the container could construct <paramref name="type"/> on its own, which is the
    /// resolver's fallback for an unregistered parameter type. A type it could not is registered
    /// with a factory that fails naming both client routes, so the failure reads as a client that
    /// has no way to be built rather than as a constructor parameter nobody registered.
    /// </summary>
    public static bool IsConstructibleByTheContainer(Type type, IServiceCollection services) {
        if (type.IsInterface || type.IsAbstract) {
            return false;
        }

        foreach (var constructor in type.GetConstructors()) {
            var satisfiable = true;

            foreach (var parameter in constructor.GetParameters()) {
                if (parameter.HasDefaultValue || IsRegistered(parameter.ParameterType, services)) {
                    continue;
                }

                satisfiable = false;

                break;
            }

            if (satisfiable) {
                return true;
            }
        }

        return false;
    }

    private static bool IsRegistered(Type type, IServiceCollection services) {
        if (type == typeof(IServiceProvider) || type == typeof(IServiceScopeFactory) || type == typeof(IServiceProviderIsService)) {
            return true;
        }

        foreach (var descriptor in services) {
            if (descriptor.ServiceType == type) {
                return true;
            }

            if (type.IsGenericType && descriptor.ServiceType.IsGenericTypeDefinition &&
                descriptor.ServiceType == type.GetGenericTypeDefinition()) {
                return true;
            }
        }

        return false;
    }

    private static ConstructorInfo? HttpClientConstructor(Type clientType) {
        var constructors = clientType.GetConstructors();

        if (constructors.Length != 1) {
            return null;
        }

        var parameters = constructors[0].GetParameters();

        return parameters.Length == 1 && parameters[0].ParameterType == typeof(HttpClient)
            ? constructors[0]
            : null;
    }

    private static ITestClientRoute? RouteFor(Type clientType, Assembly testAssembly) {
        foreach (var route in RoutesFor(testAssembly)) {
            if (route.CanBuild(clientType)) {
                return route;
            }
        }

        return null;
    }

    private static IReadOnlyList<ITestClientRoute> RoutesFor(Assembly testAssembly) =>
        Routes.GetOrAdd(testAssembly, DiscoverRoutes);

    /// <summary>The routes the assembly named that can also read answers, in the order it named them.</summary>
    public static IReadOnlyList<ITestClientReader> ReadersFor(Assembly testAssembly) =>
        Readers.GetOrAdd(testAssembly, assembly => RoutesFor(assembly).OfType<ITestClientReader>().ToArray());

    /// <summary>
    /// Every route the assembly named, in the order it named them.
    /// </summary>
    /// <remarks>
    /// A route that cannot be constructed fails here rather than when a client happens to need it,
    /// because the attribute is the declaration and a mistyped one is a mistake about this
    /// assembly rather than about any one test.
    /// </remarks>
    private static IReadOnlyList<ITestClientRoute> DiscoverRoutes(Assembly assembly) =>
        DiscoverRoutes(assembly.GetCustomAttributes<TestClientRouteAttribute>(), assembly.GetName().Name!);

    internal static IReadOnlyList<ITestClientRoute> DiscoverRoutes(
        IEnumerable<TestClientRouteAttribute> attributes, string assemblyName) {
        var routes = new List<ITestClientRoute>();

        foreach (var attribute in attributes) {
            if (!typeof(ITestClientRoute).IsAssignableFrom(attribute.RouteType)) {
                throw new InvalidOperationException(
                    $"{assemblyName} names {attribute.RouteType.FullName} as a test client " +
                    "route, and it does not implement ITestClientRoute.");
            }

            if (attribute.RouteType.GetConstructor(Type.EmptyTypes) == null) {
                throw new InvalidOperationException(
                    $"{attribute.RouteType.FullName} is named as a test client route and has no " +
                    "parameterless constructor, so the harness cannot build one.");
            }

            routes.Add((ITestClientRoute)Activator.CreateInstance(attribute.RouteType)!);
        }

        return routes;
    }

    private static Type? FactoryFor(Type clientType, Assembly testAssembly) =>
        Factories.GetOrAdd(testAssembly, Discover).TryGetValue(clientType, out var factory) ? factory : null;

    /// <summary>
    /// Every public, concrete, parameterless-constructible <see cref="ITestClientFactory{TClient}"/>
    /// in the assembly, keyed by the client it builds.
    /// </summary>
    private static IReadOnlyDictionary<Type, Type> Discover(Assembly assembly) {
        var factories = new Dictionary<Type, Type>();

        foreach (var type in assembly.GetTypes()) {
            if (!type.IsClass || type.IsAbstract || !type.IsPublic || type.GetConstructor(Type.EmptyTypes) == null) {
                continue;
            }

            foreach (var contract in type.GetInterfaces()) {
                if (contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(ITestClientFactory<>)) {
                    factories[contract.GetGenericArguments()[0]] = type;
                }
            }
        }

        return factories;
    }
}
