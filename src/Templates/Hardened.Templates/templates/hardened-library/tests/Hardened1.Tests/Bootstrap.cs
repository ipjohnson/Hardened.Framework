using Hardened.Shared.Testing.Attributes;
using Hardened1;

// The module under test. The real module graph is applied and startup services run, so there is
// no separate test wiring to keep in step with the library.
[assembly: HardenedTestEntryPoint(typeof(TemplateModuleNameLibrary))]
