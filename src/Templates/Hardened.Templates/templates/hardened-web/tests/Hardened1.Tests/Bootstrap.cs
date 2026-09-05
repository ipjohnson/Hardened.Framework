using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Testing;
using Hardened1;
#if (kestrel)
using Hardened.Web.Kestrel.Testing;
#endif
#if (aspnet)
using Hardened.Web.AspNetCore.Testing;
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
#if (kestrel || aspnet)

// The host. After this, the attribute the application names its host with - [KestrelRuntime] or
// [AspNetCoreRuntime] - runs a test carrying it on a real socket; Hardened1SocketTests does.
#endif
#if (kestrel)
[assembly: KestrelTesting]
#endif
#if (aspnet)
[assembly: AspNetCoreTesting]
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
