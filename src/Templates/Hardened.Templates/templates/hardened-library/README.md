# Hardened1

A reusable module for [Hardened](https://github.com/ipjohnson/Hardened.Framework) — a compile-time,
source-generated .NET framework. This library declares services; an application picks all of them
up with one attribute.

## Build it

```bash
dotnet build
dotnet test
```

## Using it from an application

The build writes a `[TemplateModuleNameLibrary]` attribute from the module class. An application composes it
the same way it composes a runtime:

```csharp
[HardenedModule]
[KestrelRuntime]
[TemplateModuleNameLibrary]        // everything this library registers
public partial class Application;
```

That is the whole integration. There is no `AddHardened1()` to call, no options object to thread
through, and nothing to keep in step by hand — the attribute is generated from the module, so it
cannot describe a service the library does not have.

## Referencing the library

The application needs a reference to this library before it can use the attribute. A
`ProjectReference` works when the two are scaffolded side by side:

```
Todos/          dotnet new hardened-web -n Todos
Hardened1/      dotnet new hardened-library -n Hardened1
```

Reference the library from `src/Todos/Todos.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="../../../Hardened1/src/Hardened1/Hardened1.csproj" />
</ItemGroup>
```

Put `[TemplateModuleNameLibrary]` on `TodosLibrary` in `src/Todos/TodosLibrary.cs`. The host's
`Application` already imports `[TodosLibrary]`, and imports are transitive, so the host gets this
library's services. The application's tests boot `TodosLibrary`, so they get them as well.

Build and test from the application's directory. `dotnet` reads `global.json` from the directory it
runs in, so the application's pin chooses the SDK. A solution in the directory above both scaffolds
finds neither `global.json`. To see the library in the application's solution, add it there:

```bash
dotnet sln Todos.sln add ../Hardened1/src/Hardened1/Hardened1.csproj
```

Each project still reads the `Directory.Build.props` and `Directory.Packages.props` of its own
scaffold. Scaffold both from the same template version so both pin the same Hardened version.

To share the library as a package instead, `dotnet pack src/Hardened1` and put the package on a feed
the application's `nuget.config` lists. The application then needs a `PackageVersion` for
`Hardened1` in its `Directory.Packages.props` and a `PackageReference` in place of the
`ProjectReference`.

## The two projects

| | |
|---|---|
| `src/Hardened1` | The module: its services, and nothing about where they run. |
| `tests/Hardened1.Tests` | Tests, booted against the real module. |

This library names no runtime, which is deliberate. Nothing in it mentions Kestrel, ASP.NET Core or
Lambda, so the same package works behind any of them.

## Adding to it

A service is registered where it is declared:

```csharp
[SingletonService]
public class GreetingService(IGreetingFormatter formatter) : IGreetingService {
    public string Greet(string name) => formatter.Format($"Hello, {name}");
}
```

`[SingletonService]` registers the class against every interface it implements. `[ScopedService]`
and `[TransientService]` are the other two lifetimes. The module lists nothing, so a service cannot
be written and then forgotten in a registration file somewhere else — and a dependency with no
registration is a build error rather than something the first request discovers.

To carry HTTP routes as well as services, add `[HardenedWebModule]` to the module class and
reference `Hardened.Web.Runtime` and `Hardened.Web.SourceGenerator`. The library then contributes
routes to whatever application composes it, and `[BasePath]` gives them a prefix.

## Testing

`[ModuleTest]` boots the real module and resolves the test's parameters from its container, so
what runs is the registration a consuming application would get:

```csharp
[ModuleTest]
public void GreetsByName(IGreetingService greeting) {
#if (xunit)
    Assert.Equal("Hello, world!", greeting.Greet("world"));
#else
    Assert.That(greeting.Greet("world"), Is.EqualTo("Hello, world!"));
#endif
}
```

#if (moq)
Take a `Mock<T>` parameter and that service is substituted for the whole container, including behind
another service — so the code under test is still the library's own wiring. The mock is Moq's,
through `[assembly: MoqSupport]` in `Bootstrap.cs`, and `tests/Hardened1.Tests/GreetingServiceTests.cs`
shows it.
#endif
#if (nsubstitute)
Mark a parameter `[Mock]` and that service is substituted for the whole container, including behind
another service — so the code under test is still the library's own wiring. The substitute is
NSubstitute's, through `[assembly: NSubstituteSupport]` in `Bootstrap.cs`, and
`tests/Hardened1.Tests/GreetingServiceTests.cs` shows it.
#endif
#if (fakeiteasy)
Mark a parameter `[Mock]` and that service is substituted for the whole container, including behind
another service — so the code under test is still the library's own wiring. The fake is FakeItEasy's,
through `[assembly: FakeItEasySupport]` in `Bootstrap.cs`, and
`tests/Hardened1.Tests/GreetingServiceTests.cs` shows it.
#endif

## Reading the generated code

The fastest way to understand any of this is to read what the build wrote. It is ordinary C#, and
`EmitCompilerGeneratedFiles` is already on:

```
src/Hardened1/obj/Debug/net8.0/generated/
```

The module registration and the generated attribute are both there.

## Where to go next

- [Documentation](https://ipjohnson.github.io/Hardened.Framework)
- `AGENTS.md` in this directory — the invariants and gotchas, for anyone or anything editing the
  code rather than reading it
