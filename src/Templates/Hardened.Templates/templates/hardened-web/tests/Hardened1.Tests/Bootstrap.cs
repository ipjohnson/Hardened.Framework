using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Testing;
using Hardened1;
#if (kiotaClient)
using Hardened.Kiota.Testing;
#endif

// Two assembly attributes: the harness, and the module under test. The real module graph is
// applied and startup services run, so there is no separate test wiring to keep in step.
[assembly: WebTesting]
[assembly: HardenedTestEntryPoint(typeof(TemplateModuleNameLibrary))]
#if (kiotaClient)

// And the generated client. After this every Kiota client is a test parameter, built over the
// pipeline with the test's credential on it and nothing written per client; a second service in
// this solution costs nothing.
[assembly: KiotaTesting]
#endif
