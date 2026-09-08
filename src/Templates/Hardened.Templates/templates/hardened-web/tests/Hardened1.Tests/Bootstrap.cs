#if (nsubstitute)
using DependencyModules.NSubstitute;
#endif
#if (moq)
using DependencyModules.Moq;
#endif
#if (fakeiteasy)
using DependencyModules.FakeItEasy;
#endif
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Testing;
using Hardened1;
#if (kestrel || cloudRun)
using Hardened.Web.Kestrel.Testing;
#endif
#if (aspnet)
using Hardened.Web.AspNetCore.Testing;
#endif
#if (azureFunctions)
using Hardened.Azure.Functions.Testing;
#endif
#if (kiotaClient)
using Hardened.Kiota.Testing;
#endif
#if (refitClient)
using Hardened.Refit.Testing;
#endif

// Two assembly attributes: the harness, and the module under test. The real module graph is
// applied and startup services run, so there is no separate test wiring to keep in step.
[assembly: WebTesting]
[assembly: HardenedTestEntryPoint(typeof(TemplateModuleNameLibrary))]

// The mock library. [Mock] on a parameter asks this attribute for the double and builds nothing
// itself, so without it a [Mock] parameter fails with "Mock library not found". The package that
// carries it is in Hardened1.Tests.csproj.
#if (nsubstitute)
[assembly: NSubstituteSupport]
#endif
#if (moq)
[assembly: MoqSupport]
#endif
#if (fakeiteasy)
[assembly: FakeItEasySupport]
#endif
#if (kestrel || aspnet || cloudRun)

// The host. After this, the attribute the application names its host with - [KestrelRuntime] or
// [AspNetCoreRuntime] - runs a test carrying it on a real socket; Hardened1SocketTests does.
#endif
#if (kestrel || cloudRun)
[assembly: KestrelTesting]
#endif
#if (aspnet)
[assembly: AspNetCoreTesting]
#endif
#if (azureFunctions)

// The host. Every test's request is built as the worker's own request data and goes through the
// real invocation handler, the HTTP adapter and the routing table, and the answer comes back as
// the worker's response data - so the adapter is exercised without a Functions host process.
// Delete this line and the same tests run on the pipeline alone.
[assembly: AzureFunctionsWebTesting]
#endif
#if (hasClient)

// And the generated client. After this every client of the generator's shape is a test parameter,
// built over the pipeline with the test's credential on it and nothing written per client; a
// second service in this solution costs nothing.
#endif
#if (kiotaClient)
[assembly: KiotaTesting]
#endif
#if (refitClient)
[assembly: RefitTesting]
#endif
