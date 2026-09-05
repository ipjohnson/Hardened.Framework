# Hardened1

Invariants and traps for anyone editing this code. `README.md` covers what this library is, how to
build it and how an application consumes it; this file does not repeat any of that.

## Most of this library is generated at build time

Hardened is a compile-time framework. The module's registrations, and the `[TemplateModuleNameLibrary]`
attribute applications apply, are written by source generators during the build rather than
resolved by reflection at run time.

**They are ordinary C#, and you can read them.** `EmitCompilerGeneratedFiles` is on:

```
src/Hardened1/obj/Debug/net8.0/generated/     one directory per generator
```

Build first. Reading that directory answers most "how does this work" questions faster than reading
the framework.

## The one structural rule

**This library names no runtime.** Nothing in `src/Hardened1` may reference Kestrel, ASP.NET Core or
Lambda, or a Hardened package that does. That is what lets one package serve every host, and it is
the first thing to check when a consumer reports that it will not compose.

## Things that will not be obvious

**The module class stays empty, and stays `partial`.** The generator writes the other half. A
service registers itself where it is declared, so there is no list in the module to keep in step —
adding one is the mistake, not the fix.

**The source generator package is required.** The runtime packages carry no analyzers, so removing
`Hardened.Library.SourceGenerator` does not fail with a missing package — it fails with
`'TemplateModuleNameLibrary' does not contain a definition for 'PopulateServiceCollection'`, and consumers
lose the attribute entirely. Versions are pinned in one place, `Directory.Packages.props`.

#if (xunit)
**Tests are xUnit v3.** `Hardened.Shared.Testing.xUnit` builds on `xunit.v3.extensibility.core`; a
test project on xunit 2.x fails with `CS0433` on `Assert`. v3 test projects are also self-executing,
hence `<OutputType>Exe</OutputType>`. `Hardened.Shared.Testing.NUnit` is the other runner, and
`--test-framework nunit` scaffolds for it.
#endif
#if (nunit)
**Tests are NUnit 4.** `Hardened.Shared.Testing.NUnit` takes NUnit as `[4.2.2, 5.0.0)`, and
`[HardenedTest]` is NUnit's own test attribute underneath, so the adapter discovers it with no
`[Test]` beside it. `Hardened.Shared.Testing.xUnit` is the other runner, and `--test-framework xunit`
scaffolds for it.
#endif

#if (nsubstitute)
**`[Mock]` is DependencyModules' attribute, and NSubstitute answers it.** The attribute builds
nothing itself: `[assembly: NSubstituteSupport]` in `tests/Hardened1.Tests/Bootstrap.cs` supplies the
double, and `DependencyModules.NSubstitute` is the package that carries both the attribute and
NSubstitute. Remove either and a `[Mock]` parameter fails with "Mock library not found".
`--mocks moq` and `--mocks fakeiteasy` scaffold the other two libraries.
#endif
#if (moq)
**`[Mock]` is DependencyModules' attribute, and Moq answers it.** `[assembly: MoqSupport]` in
`tests/Hardened1.Tests/Bootstrap.cs` supplies the double, and `DependencyModules.Moq` is the package
that carries both the attribute and Moq. A `Mock<T>` parameter is the mock to configure, and the
container is given its `Object`; a parameter typed as the service and marked `[Mock]` receives that
`Object`. Remove the attribute and a `[Mock]` parameter fails with "Mock library not found".
`--mocks nsubstitute` and `--mocks fakeiteasy` scaffold the other two libraries.
#endif
#if (fakeiteasy)
**`[Mock]` is DependencyModules' attribute, and FakeItEasy answers it.** The attribute builds
nothing itself: `[assembly: FakeItEasySupport]` in `tests/Hardened1.Tests/Bootstrap.cs` supplies the
fake, and `DependencyModules.FakeItEasy` is the package that carries both the attribute and
FakeItEasy. Remove either and a `[Mock]` parameter fails with "Mock library not found".
`--mocks nsubstitute` and `--mocks moq` scaffold the other two libraries.
#endif

**`[HardenedTest]` boots the real module.** Test method parameters are resolved from its container.
#if (moq)
A `Mock<T>` parameter substitutes a service there rather than in the test, which is why a
substituted dependency is still used by the real service resolved alongside it.
#else
`[Mock]` substitutes a service there rather than in the test, which is why a substituted dependency
is still used by the real service resolved alongside it.
#endif

## Commands

See `README.md`. Nothing here overrides it.
