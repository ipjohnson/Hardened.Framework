# Hardened.Templates.RazorBlade

Renders Razor templates for handlers annotated with `[Template]`.

RazorBlade compiles `.cshtml` to C# at build time and has no dependency on ASP.NET Core — no
`Microsoft.AspNetCore.App` framework reference, no MVC types in the generated code. That is what
makes it usable from the Lambda runtimes, which are plain `Microsoft.NET.Sdk` projects.

## Wiring

```csharp
[Template("Fortunes")]
public Task<FortunePage> GetFortunes() => _repository.LoadAsync();
```

`[Template]` puts the name on `IExecutionResponse.TemplateName` before the handler runs.
`TemplateResponseSerializer` picks it up and hands it to whichever `ITemplateEngine` claims it.

Templates are registered by name, because a name is what arrives at run time:

```csharp
[SingletonService]
public class AppTemplates : IRazorBladeTemplateSource {
    public IEnumerable<RazorBladeTemplateDescriptor> Templates => [
        RazorBladeTemplate.Html<FortunePage>("Fortunes", model => new Views.Fortunes(model)),
        RazorBladeTemplate.PlainText<Receipt>("Receipt", model => new Views.Receipt(model))
    ];
}
```

The lambda is where the untyped model becomes a typed one. The cast is written by the compiler
rather than by reflection, so this stays AOT-clean.

Registration lives in the application because RazorBlade generates template classes as `internal`
by default — nothing outside that assembly can name them. Set `RazorBladeDefaultAccessibility` to
`public` if a library needs to ship views.

Apply the module alongside the rest, listed after the web module:

```csharp
[HardenedModule]
[HardenedWebModule]
[KestrelRuntime]
[RazorBladeTemplateLibrary]
public partial class Application { }
```

Order is worth knowing about. `SerializationLocatorService` tests later registrations first, so a
request carrying both `Accept: application/json` and a template name resolves to whichever
serializer was registered second. If a handler you expected to render a view returns JSON instead,
that ordering is the first thing to check.

## Content type

Taken from the template's base type, not from the file extension:

| Base type | Content type |
|---|---|
| `HtmlTemplate<T>` | `text/html; charset=utf-8` |
| `PlainTextTemplate<T>` | `text/plain; charset=utf-8` |
| anything, via `RazorBladeTemplate.Create` | whatever you pass |

A handler that sets `Response.ContentType` itself keeps it — the engine only fills in a blank.

## Two things that will catch you out

**Reference `RazorBlade` directly in the application.** RazorBlade ships `build/` but no
`buildTransitive/`, and MSBuild props do not flow transitively. Referencing only
`Hardened.Templates.RazorBlade` means the `.props` that globs `**/*.cshtml` never reaches your
project, and your templates compile to nothing with no error.

```xml
<PackageReference Include="RazorBlade" Version="1.0.0" />
<PackageReference Include="Hardened.Templates.RazorBlade" Version="..." />
```

**Kestrel and ASP.NET Core hosts need one Razor generator, not two.** RazorBlade emits warning
RB0006 when `UsingMicrosoftNETSdkRazor` is true, because its generator and the SDK's would both
process the same `.cshtml`. Set `EnableDefaultRazorBladeItems=false` and list templates explicitly,
or keep views out of the Razor SDK's default globs.

## Naming

If your own assembly's namespace ends in `RazorBlade`, qualify the base type in `.cshtml` as
`@inherits global::RazorBlade.HtmlTemplate<TModel>`. Without `global::`, the name binds to the
enclosing namespace and fails to resolve.
