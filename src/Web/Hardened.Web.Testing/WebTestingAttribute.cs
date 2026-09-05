using DependencyModules.Testing.Attributes.Interfaces;
using DependencyModules.Testing.Impl;
using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Requests.Runtime.Errors;
using Hardened.Requests.Testing;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Runtime.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Hardened.Web.Testing;

/// <summary>
/// The web harness: <see cref="ITestWebApp"/>, the test credential, and a typed client for every
/// test parameter that names one.
/// </summary>
/// <remarks>
/// <para>
/// A test declares <c>TodosClient client</c> and gets one built over the pipeline with the
/// credential the attributes in scope resolve to. This attribute already sees the test method
/// when it sets up the service collection, so it registers an instance per client parameter and
/// ordinary resolution does the rest - no new hook in the runner. Two parameters of one type with
/// different <see cref="GrantsAttribute"/>s are two instances, because a parameter attribute is
/// asked for its own value and builds one with its own credential.
/// </para>
/// <para>
/// <see cref="TestGrantsPrincipalSource"/> is registered beside whatever sources the application
/// has, so the attributes work in any test project. It answers only a request carrying the test
/// headers, and <c>AuthenticationMiddleware</c> asks each source in turn until one answers, so an
/// application's own source still authenticates its own way in a test with no attributes -
/// <c>CredentialTests</c> holds it to that rather than assuming it.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly)]
public class WebTestingAttribute : Attribute, ITestServiceSetupAttribute, ITestStartupAttribute {
    public void SetupServiceCollection(ITestMethodContext testMethod, IServiceCollection serviceCollection) {
        // The harness is a terminal host: there is nothing behind it to hand an unmatched request
        // to, so a path with no route is a 404 here, exactly as it is on Kestrel and on Lambda.
        //
        // It has to be stated rather than inherited, because the application under test names its
        // deployment runtime and that runtime's policy arrives with it. An application carrying
        // [AspNetCoreRuntime] registers AspNetResourceNotFoundHandler, which deliberately leaves
        // the status unset so UseHardened() can defer to the rest of the ASP.NET pipeline. Correct
        // there; wrong here, where deferring means answering nothing at all.
        //
        // Registration attributes run after the application's modules, which is what lets this win.
        serviceCollection.RemoveAll<IResourceNotFoundHandler>();
        serviceCollection.AddSingleton<IResourceNotFoundHandler, ResourceNotFoundHandler>();

        // Beside the application's own sources rather than instead of them. TryAddEnumerable
        // registers this implementation once however many test projects' attributes run, and the
        // middleware asks the sources in registration order, so a request carrying no test header
        // is declined here and answered by the application's source as it always was.
        serviceCollection.TryAddEnumerable(
            ServiceDescriptor.Singleton<IPrincipalSource, TestGrantsPrincipalSource>());

        var declaringType = testMethod.Method.DeclaringType!;
        var testAssembly = declaringType.Assembly;
        var credential = TestCredential.Resolve(testMethod);

        serviceCollection.AddTransient<ITestWebApp>(sp => {
            var loggerType = typeof(ILogger<>).MakeGenericType(declaringType);
            var logger = (ILogger)sp.GetRequiredService(loggerType);
            var appRoot = sp.GetRequiredService<IApplicationRoot>();
            return new TestWebApp(appRoot, logger, credential, testAssembly);
        });

        RegisterClientParameters(testMethod, serviceCollection, credential, testAssembly);
    }

    /// <summary>
    /// A scoped instance for every parameter whose type is neither registered nor one the harness
    /// supplies, built over an <see cref="HttpClient"/> carrying the method's credential.
    /// </summary>
    /// <remarks>
    /// A parameter carrying a credential attribute of its own is left to that attribute, which
    /// builds the instance with the narrower credential. A parameter with another value provider,
    /// <c>[Mock]</c> for one, is that provider's. A type with neither construction route that the
    /// container could not build on its own is registered to fail naming both routes, so the
    /// message names the fix rather than a constructor parameter nobody registered; a type the
    /// container can build is left to it. A later registration of the same type replaces this
    /// one, as any registration does, but not one made with <c>TryAdd</c>.
    /// </remarks>
    private static void RegisterClientParameters(
        ITestMethodContext testMethod,
        IServiceCollection serviceCollection,
        TestCredential credential,
        System.Reflection.Assembly testAssembly) {
        foreach (var parameter in testMethod.Method.GetParameters()) {
            var type = parameter.ParameterType;

            if (type == typeof(IServiceProvider) ||
                parameter.GetCustomAttributes(inherit: true).OfType<ITestParameterValueProvider>().Any() ||
                serviceCollection.Any(descriptor => descriptor.ServiceType == type)) {
                continue;
            }

            if (type == typeof(HttpClient)) {
                serviceCollection.AddScoped(type, sp =>
                    TestClientBuilder.CreateHttpClient(sp.GetRequiredService<IApplicationRoot>().Provider, credential));

                continue;
            }

            if (TestClientBuilder.HasRoute(type, testAssembly)) {
                serviceCollection.AddScoped(type, sp => TestClientBuilder.Build(
                    type,
                    TestClientBuilder.CreateContext(sp.GetRequiredService<IApplicationRoot>().Provider, credential),
                    testAssembly));

                continue;
            }

            if (!TestClientBuilder.IsConstructibleByTheContainer(type, serviceCollection)) {
                var message = TestClientBuilder.NoRouteMessage(type, testAssembly);

                serviceCollection.AddScoped(type, _ => throw new InvalidOperationException(message));
            }
        }
    }

    public async Task StartupAsync(ITestMethodContext testMethod, IServiceProvider serviceProvider) {
        var entryPoint = testMethod.Method.GetTestAttribute<HardenedTestEntryPointAttribute>();

        // Through the guard rather than a loop of this attribute's own. HardenedTestEntryPoint
        // runs the same services through ApplicationLogic.Start, and the runner awaits both
        // attributes in an order that is a sort rather than a declaration, so a loop here ran
        // every startup service a second time per container: the authentication middleware and
        // the CORS filter were each in the chain twice, behind the handler, and the authorization
        // filter provider was registered twice. The guard runs them once whichever attribute the
        // runner reaches first, and still runs them here for a test with no entry point attribute.
        await ApplicationLogic.Start(serviceProvider, null);

        if (entryPoint != null && !typeof(IApplicationRoot).IsAssignableFrom(entryPoint.EntryPoint)) {
            var handler = serviceProvider.GetRequiredService<IWebExecutionHandlerService>();
            var middleware = serviceProvider.GetRequiredService<IMiddlewareService>();
            middleware.Use(_ => handler);
        }
    }
}
