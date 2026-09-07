# Hardened1

Invariants and traps for anyone editing this code. `README.md` covers what this function is, how to
build it and how the handler works; this file does not repeat any of that.

## Most of this function is generated at build time

Hardened is a compile-time framework. The Lambda entry point, the payload binding, the filter chain
and the dependency injection registrations are written by source generators during the build, not
resolved by reflection at run time.

**They are ordinary C#, and you can read them.** `EmitCompilerGeneratedFiles` is on:

```
src/Hardened1/obj/Debug/net8.0/generated/     one directory per generator
```

Build first. Reading that directory answers most "how does this work" questions faster than reading
the framework.

## There is no Program.cs, and there should not be one

The generator writes the entry point AWS invokes. Adding a `Main` does not override it — it gives
the assembly a second entry point and the build fails. Local invocation goes through the test
project, which is why the tests are the way this function is run.

## Things that will not be obvious

**`[assembly: LambdaFunctionTesting]` in the test project is load-bearing.** It registers the invoke
filter provider and, at startup, puts the invoke filter into the chain. Without it the pipeline
holds no filters at all, so an invocation builds a chain of length zero, returns an empty stream and
never reaches the handler — with no error anywhere. A test that suddenly asserts against nothing is
the symptom.

**The source generator packages are required.** The runtime packages carry no analyzers, so removing
`Hardened.Library.SourceGenerator` or `Hardened.Function.SourceGenerator` does not fail
with a missing package — it fails with `'Application' does not contain a definition for
'PopulateServiceCollection'`, or it builds clean and the function has no entry point. All versions
are pinned in one place, `Directory.Packages.props`.

**One package line, one version.** Every `Hardened.*` package, host adapters included, releases
together on `HardenedVersion` in `Directory.Packages.props`. There is no second line to keep in
step: the AWS packages are part of the framework repository rather than a separate release, which
is what the old `Hardened.Amz.*` pin was and what let a template name a generator that no longer
matched the interface it emitted against.

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

**`[HardenedTest]` boots the real application.** Test method parameters are resolved from the
application's own container, and the test harness drives the real invocation path.
#if (moq)
Take a `Mock<T>` parameter to substitute a service for the whole application.
#else
Mark a parameter `[Mock]` to substitute a service for the whole application.
#endif

## Commands

See `README.md`. Nothing here overrides it.
