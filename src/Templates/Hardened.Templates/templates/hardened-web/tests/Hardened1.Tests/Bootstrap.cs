using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Testing;
using Hardened1;

// Two assembly attributes: the harness, and the module under test. The real module graph is
// applied and startup services run, so there is no separate test wiring to keep in step.
[assembly: WebTesting]
[assembly: HardenedTestEntryPoint(typeof(TemplateModuleNameLibrary))]
