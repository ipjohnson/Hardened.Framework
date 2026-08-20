# Hardened1

Invariants and traps for anyone editing this code. `README.md` covers what the application is, how
to run it and how the projects fit together; this file does not repeat any of that.

## Most of this application is generated at build time

Hardened is a compile-time framework. Routing tables, request handlers, parameter binding,
dependency injection registrations and configuration implementations are written by source
generators during the build, not resolved by reflection at run time.

**They are ordinary C#, and you can read them.** `EmitCompilerGeneratedFiles` is on:

```
src/Hardened1/obj/Debug/net8.0/generated/     one directory per generator
```

Build first. Reading that directory answers most "how does this work" questions faster than
reading the framework.

#if (specFirst)
## Routes are not in the C#

There are no route attributes anywhere in this project. **Do not add them, and do not look for
them.** The verb, path and shapes come from the contract:

#if (openapi)
```
src/Hardened1/contracts/greeting.yaml
```
#endif
#if (smithy)
```
src/Hardened1/contracts/greeting.smithy
```
#endif

The build turns it into models, a service interface, a routing table and the validation its
constraints describe. `src/Hardened1/GreetingService.cs` implements that interface and is marked
`[Handler]`, which is what the generated routing points at.

To add or change an endpoint, edit the contract. The interface changes with it, and the
implementation stops compiling until it matches — that is the design, not a break.

#if (openapi)
Generated types land in `Hardened1.Models`, `Hardened1.Services` and `Hardened1.Validation`.
#endif
#if (smithy)
Generated types land in `Hardened1.Models`, `Hardened1.Services` and `Hardened1.Validation`. The
build needs the Smithy CLI on `PATH` at the pinned version; a mismatch fails with `HSMT011`
naming both versions, because different CLI versions can produce different ASTs.
#endif
#endif
#if (codeFirst)
## Routes are attributes on plain classes

`src/Hardened1/GreetingController.cs`. No base type, no interface, no registration — the generator
finds `[Get]`, `[Post]`, `[Put]`, `[Delete]` or `[Patch]` and emits a handler bound to that
method's exact signature.

`[BasePath]` on `TemplateModuleNameLibrary` prefixes every route in the assembly, so a route of `/{name}`
is served at `/greeting/{name}`.
#endif

## The one structural rule

**The implementation library must stay host-independent.** Nothing in `src/Hardened1` may reference
the host project or name a runtime. Swapping `src/Hardened1.Host` is expected to change no file
outside it, and the tests target the library for the same reason.

## Things that will not be obvious

**The source generator packages are required.** The runtime packages carry no analyzers, so
removing `Hardened.Library.SourceGenerator` or the routing generator from a csproj does not fail
with a missing package — it fails with `'Application' does not contain a definition for
'PopulateServiceCollection'`, or it builds clean and answers 404 to everything. All package
versions are pinned in one place, `Directory.Packages.props`.

**The environment is registered under two interfaces**, and both are load-bearing.
`IHardenedEnvironment` is what application code reads; `IModuleEnvironment` is what decides which
services are registered at all (`[IfEnvironment]`). They are looked up separately, and dropping
the second silently gives you `Production` while everything else says `development`. See the
comment in `Program.cs`.

**Tests are xUnit v3.** `Hardened.Shared.Testing` builds on `xunit.v3.extensibility.core`; a test
project on xunit 2.x fails with `CS0433` on `Assert`. v3 test projects are also self-executing,
hence `<OutputType>Exe</OutputType>`.

**`[HardenedTest]` boots the real application.** Test method parameters are resolved from the
application's own container, and `ITestWebApp` drives the real pipeline — routing, filters,
binding, serialisation — without a socket or a port. Mark a parameter `[Mock]` to substitute a
service.

## Commands

See `README.md`. Nothing here overrides it.
