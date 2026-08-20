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
`Hardened.Library.SourceGenerator` or `Hardened.Amz.Function.Lambda.SourceGenerator` does not fail
with a missing package — it fails with `'Application' does not contain a definition for
'PopulateServiceCollection'`, or it builds clean and the function has no entry point. All versions
are pinned in one place, `Directory.Packages.props`.

**Two package lines, one version.** `Hardened.*` and `Hardened.Amz.*` release together, and
`Directory.Packages.props` pins the second to the first through `HardenedAmzVersion`. If they ever
diverge, that is the one line to change.

**Tests are xUnit v3.** `Hardened.Shared.Testing` builds on `xunit.v3.extensibility.core`; a test
project on xunit 2.x fails with `CS0433` on `Assert`. v3 test projects are also self-executing,
hence `<OutputType>Exe</OutputType>`.

**`[HardenedTest]` boots the real application.** Test method parameters are resolved from the
application's own container, and the test harness drives the real invocation path. Mark a parameter
`[Mock]` to substitute a service.

## Commands

See `README.md`. Nothing here overrides it.
