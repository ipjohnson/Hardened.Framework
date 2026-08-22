# Hardened1

Invariants and traps for anyone editing this code. `README.md` covers what the application is, how
to run it and how the projects fit together; this file does not repeat any of that.

## Build before you read anything

Most of this application does not exist until the build writes it. Routing tables, request
handlers, parameter binding, dependency injection registrations
#if (specFirst)
, the service interface and every model
#endif
are generated during compilation.

**A project that has never been built looks broken.** You will see `CS0246` on types nothing
declares and `'Application' does not contain a definition for 'PopulateServiceCollection'`. Nothing
is wrong. Run `dotnet build` first.

**They are ordinary C#, and reading them answers most questions faster than reading the framework:**

```
src/Hardened1/obj/Debug/net8.0/generated/     one directory per generator
#if (specFirst)
src/Hardened1/obj/Debug/net8.0/openapi/       the normalised contract and the code built from it
#endif
```

**Never edit anything under `obj/`.** It is regenerated on the next build and your change is gone.
Change the thing it was generated from.

## When something looks wrong

| Symptom | What it means |
|---|---|
| `CS0246` on a type you did not write | Never built. Build first. |
| `'Application' does not contain 'PopulateServiceCollection'` | A `*.SourceGenerator` package was removed from a csproj |
| Duplicate definitions under `obj/**/generated/` | A generator was renamed and its old output is still there. Delete `obj/`. |
| Builds clean, every route answers 404 | The routing generator is absent. The runtime packages carry no analyzers. |
| A handler parameter arrives empty, or `CS0128` on `contentSerializationService` | A concrete class as a handler parameter is bound from the request body. Inject an interface, or mark it `[FromServices]`. |
#if (specFirst)
| `HOAT001` naming a contract that exists | The path in the csproj does not match the file |
#endif
#if (smithy)
| `HSMT011` | The Smithy CLI on `PATH` is a different version from the pin |
#endif
#if (declaredMode)
| `HRDRM003` / `HRDRM004` | A response case is `object`, or two cases at different statuses are assignable to one another |
#endif

#if (specFirst)
## Routes are not in the C#

There are no route attributes anywhere in this project. **Do not add them, and do not look for
them.** The verb, the path, the shapes and the statuses come from the contract:

#if (openapi)
```
src/Hardened1/contracts/todos.yaml
```
#endif
#if (smithy)
```
src/Hardened1/contracts/todos.smithy
```
#endif

The build turns it into models, a service interface, a routing table and the validation its
constraints describe. `src/Hardened1/TodoService.cs` implements that interface and is marked
`[Handler]`, which is what the generated routing points at.

To add or change an endpoint, edit the contract. The interface changes with it, and the
implementation stops compiling until it matches — that is the design, not a break.

Generated types land in `Hardened1.Models`, `Hardened1.Services` and `Hardened1.Validation`.
#if (smithy)
The build needs the Smithy CLI on `PATH` at the pinned version; a mismatch fails with `HSMT011`
naming both versions, because different CLI versions can produce different ASTs.
#endif
#endif
#if (codeFirst)
## Routes are attributes on plain classes

`src/Hardened1/TodoController.cs`. No base type, no interface, no registration — the generator
finds `[Get]`, `[Post]`, `[Put]`, `[Delete]` or `[Patch]` and emits a handler bound to that
method's exact signature.

`[BasePath]` on `TemplateModuleNameLibrary` prefixes every route in the assembly, so a route of
`/{id}` is served at `/todos/{id}`.

**Services arrive as handler parameters**, alongside route and body values — `ITodoStore store` in
every handler here. Ask for an interface. A parameter typed as a concrete class is bound from the
request body instead, which on a route that also takes a body generates two body reads and does not
compile.
#endif

## How this application declares its responses

#if (standardMode)
**Standard.** Each handler names one success type, and every other status is thrown:

```csharp
throw new NotFound("todo", $"No todo has id {id}.").AsException();
```

The thrown value is an ordinary response record, so the body a client sees is the same one the
declared modes return. What differs is that nothing in the signature says the route can answer it.

#if (codeFirst)
This mode cannot put a status beside its success type, which is why `Create` answers 200 rather
than 201. Regenerate with `--response-model response` and the same route answers 201.
#endif
#if (specFirst)
The contract still names each operation's success status and the dispatch carries it, so `POST
/todos` answers 201 here. What this mode cannot express is *more than one* success status.
#endif

To add an error path: throw. To make it part of the contract, move to `--response-model response`.
#endif
#if (declaredMode)
#if (responseMode)
**Response.** Each handler returns the whole set it can answer with:

```csharp
public Response<Todo, NotFound> ById(ITodoStore store, int id)
```
#endif
#if (unionMode)
**Union.** Each handler returns a C# 15 union naming the whole set it can answer with:

```csharp
public union TodoResult(Todo, NotFound);

public TodoResult ById(ITodoStore store, int id)
```

This needs the .NET 11 SDK and `LangVersion preview`, both pinned — `global.json` and
`src/Hardened1/Hardened1.csproj`. A union is not only a compiler feature: it needs `IUnion` and
`UnionAttribute`, which arrive with the .NET 11 reference assemblies, which is why the framework is
`net11.0` here and `net8.0` everywhere else.
#endif

There is one implicit conversion per case, so a handler returns the payload and never names the
wrapper. Returning the wrong status for a route is a compile error rather than a wrong answer, and
the generated document describes every declared status because every one of them is in the
signature.

To add an error path: add a case to the return type, then return it. The compiler finds every place
that has to change.

**A case type may not appear twice in one set.** Two identical type arguments give two identical
conversions and the compiler rejects the use site — which is why `NotFound` appearing in two
different response sets is fine and appearing twice in one is not.
#endif

## The one structural rule

**The implementation library must stay host-independent.** Nothing in `src/Hardened1` may reference
the host project or name a runtime. Swapping `src/Hardened1.Host` is expected to change no file
outside it, and the tests target the library for the same reason.

## Things that will not be obvious

**The source generator packages are required.** The runtime packages carry no analyzers, so
removing one from a csproj does not fail with a missing package — it fails with a missing generated
member, or it builds clean and answers 404 to everything. All package versions are pinned in one
place, `Directory.Packages.props`.

**`global.json` is load-bearing.** Without it the newest installed SDK wins, and a `net8.0` project
built by a .NET 11 preview SDK writes a test `deps.json` missing its own project reference — the
build succeeds and every test fails at once with `FileNotFoundException` on the assembly under test.

**The environment is registered under two interfaces**, and both are load-bearing.
`IHardenedEnvironment` is what application code reads; `IModuleEnvironment` is what decides which
services are registered at all (`[IfEnvironment]`). They are looked up separately, and dropping the
second silently gives you `Production` while everything else says `development`. See the comment in
`Program.cs`.

**Tests are xUnit v3.** `Hardened.Shared.Testing` builds on `xunit.v3.extensibility.core`; a test
project on xunit 2.x fails with `CS0433` on `Assert`. v3 test projects are also self-executing,
hence `<OutputType>Exe</OutputType>`.

#if (codeFirst)
**JSON is configured by registering a resolver, not by editing options.** One line in
`TemplateModuleNameLibrary.ConfigureServices` registers `TemplateModuleNameJsonContext` as an
`IJsonTypeInfoResolver`, and every JSON serializer in the pipeline reads it — request body,
response body, streamed item. Adding a type to the wire means adding a `[JsonSerializable]` line
to that context.

**An enum's wire vocabulary is `[JsonEnumNaming]`, and it governs the document too.** The build
writes a converter for every enum this application serializes and registers it for both the JSON
body and the parameter binder, and the `enum` array in the generated OpenAPI description is written
from the same setting — so the contract a client generates against cannot disagree with the bytes
this application produces.

The default is camelCase: `InProgress` goes out as `"inProgress"`. To choose something else, set it
for the assembly, for one enum, or both:

```csharp
[assembly: JsonEnumNaming(EnumNaming.KebabCaseLower)]   // "in-progress"

[JsonEnumNaming(EnumNaming.MemberName)]                 // opts out: "AB12"
public enum LegacyCode { AB12, CD34 }
```

**Decide this before the first client.** Changing an enum's vocabulary later breaks every consumer
and no compiler will say so.

Only enums this assembly declares are given a vocabulary. A `[Flags]` enum is left alone — a
combination of members has no single member name to write — and so is anything from a referenced
framework, since renaming those would redefine a contract that is not this application's.

**Do not reach for `[JsonConverter(typeof(JsonStringEnumConverter))]`.** It writes the C# member
name rather than a wire value, it never reaches the published document, and the non-generic form
cannot work under Native AOT — so it fails when the application is published rather than when it is
written. `SYSLIB1034` says so where the compiler can see it.

#endif

**`[HardenedTest]` boots the real application.** Test method parameters are resolved from the
application's own container, and `ITestWebApp` drives the real pipeline — routing, filters,
binding, serialisation — without a socket or a port. Mark a parameter `[Mock]` to substitute a
service. Note the argument order: `app.Post(value, path)` takes the body first.

## Commands

See `README.md`. Nothing here overrides it.
