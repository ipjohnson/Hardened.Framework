# Hardened1

Invariants and traps for anyone editing this code. `README.md` covers what this function is, how to
build it and how the handler works; this file does not repeat any of that.

## Most of this function is generated at build time

Hardened is a compile-time framework. The dispatch to the handler, the payload binding, the filter
chain and the dependency injection registrations are written by source generators during the
build, not resolved by reflection at run time.

**They are ordinary C#, and you can read them.** `EmitCompilerGeneratedFiles` is on:

```
src/Hardened1/obj/Debug/net8.0/generated/     one directory per generator
```

Build first. Reading that directory answers most "how does this work" questions faster than reading
the framework.

## Program.cs is written, not generated

`src/Hardened1/Program.cs` is the entry point, and it is short on purpose: it builds the container
#if (aws)
and starts the invocation loop. Nothing generates a `Main`, so a second entry point is a compile
error, and deleting this one leaves a deployed function that fails on its first invocation with
"Entry point not found". The call to `LambdaEmulator.StartIfLocal` in it is what runs the function
locally; delete that line and the function still deploys.
#endif
#if (gcp)
and starts Kestrel on `PORT`. Nothing generates a `Main`, so a second entry point is a compile
error, and deleting this one leaves a container with nothing to start. `CloudRunHost.Listen` and
`CloudRunHost.RunAsync` are the container contract, the port and the `SIGTERM` drain; replace them
with the plain Kestrel calls and a request in flight when Cloud Run retires the instance is cut
off.
#endif

## Things that will not be obvious

**`[assembly: FunctionTesting]` in the test project is load-bearing.** It is what makes the
generated façades resolvable, so a test taking `Application.Queues` or `Application.Invocations`
as a parameter fails to resolve without it rather than running nothing.
#if (aws)
`[assembly: LambdaTesting]` beside it raises the fidelity: the payload is packed into the envelope
AWS sends and goes in through the invocation loop. No test method reads differently either way.
#endif
#if (gcp)
`[assembly: CloudRunTesting]` beside it raises the fidelity: the payload is packed into the request
Google sends and posted to the test's web host. **It needs `[assembly: WebTesting]` beside it**,
because that is what registers the host the request is posted to; the delivery says so if it is
missing rather than running nothing. No test method reads differently either way.
#endif

**The source generator packages are required.** The runtime packages carry no analyzers, so removing
`Hardened.Library.SourceGenerator` or `Hardened.Function.SourceGenerator` does not fail
with a missing package — it fails with `'Application' does not contain a definition for
'PopulateServiceCollection'`, or it builds clean and the function has no dispatch. All versions
are pinned in one place, `Directory.Packages.props`.

**One package line, one version.** Every `Hardened.*` package, host adapters included, releases
together on `HardenedVersion` in `Directory.Packages.props`. There is no second line to keep in
step: the cloud packages are part of the framework repository rather than a separate release, which
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
application's own container, and the test harness drives the real delivery path.
#if (moq)
Take a `Mock<T>` parameter to substitute a service for the whole application.
#else
Mark a parameter `[Mock]` to substitute a service for the whole application.
#endif

## Commands

See `README.md`. Nothing here overrides it.
