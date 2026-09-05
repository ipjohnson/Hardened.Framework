# Hardened.Templates.RazorBlade

Renders Razor views for handlers annotated with `[Output<T>]`.

RazorBlade compiles `.cshtml` to C# at build time and has no dependency on ASP.NET Core — no
`Microsoft.AspNetCore.App` framework reference, no MVC types in the generated code. The same views
work under Kestrel, ASP.NET Core and the Lambda runtimes.

## Wiring

Turn it on by naming the marker, which is what references the package:

```csharp
[HardenedModule]
[HardenedWebModule]
[KestrelRuntime]
[Enable<RazorTemplates>]
public partial class Application { }
```

That generates `ApplicationRazorTemplates<TModel>` — the entry point's name plus the marker's — for
views to inherit:

```razor
@inherits Application.ApplicationRazorTemplates<FortunePage>
<table>@foreach (var fortune in Model.Fortunes) { <tr><td>@fortune.Message</td></tr> }</table>
```

and a handler names the view by type:

```csharp
[Get("/fortunes")]
[Output<Views.Fortunes>]
public FortunePage GetFortunes() => _repository.Load();
```

There is nothing to register. The generated handler puts a factory on the response, the
serialization filter builds the view, hands it the model and asks it to render.

## What the compiler checks, and where

**On the attribute.** `OutputAttribute<T>` constrains `T` to `IHardenedResponseOutput, new()`, and
the attribute is applied in your own assembly, where RazorBlade's output exists. A type that is not
an output, or has no parameterless constructor, is an error on the attribute, naming the type. It is
also why the view is named by type rather than by string: RazorBlade's `internal` generated classes
are nameable from your assembly.

**In generated code.** That the view's model matches the handler's return type cannot be expressed
on the attribute, and the generator cannot check it because the view is another generator's output.
So the generator emits an assignment the compiler has to bind:

```csharp
private static readonly IHardenedResponseOutput<FortunePage> _outputCheck_GetFortunes = new Views.Fortunes();
```

A mismatch reads "cannot convert Views.Fortunes to IHardenedResponseOutput<FortunePage>", naming
both types.

## Choosing a view per request

The response carries a factory, assigned before the handler runs, so a handler or filter can replace
it — a different view for mobile than for desktop, an A/B test, an error view:

```csharp
context.Response.OutputFactory = static _ => new Views.FortunesMobile();
```

One construction shape only: the view is constructed with no arguments and the model attached
afterwards.

## Specification-first

The same attribute works on the implementation of a spec-generated service interface. The document
declares that the operation answers with `text/html`, and the attribute names the view that produces
it, so changing engines or views does not edit the API description. There is no spec extension for
this.

## Content type

From the marker, which is to say from the base class:

| Marker | Base | Content type |
|---|---|---|
| `RazorTemplates` | `HardenedHtmlTemplate<T>` | `text/html; charset=utf-8` |

A handler that sets `Response.ContentType` itself keeps it — rendering only fills in a blank.

Another engine ships its own marker with its own `[TemplateBase]` and `[TemplateContentType]` and
needs no change to Hardened. Two markers on one module produce two bases.

## Two things that will catch you out

**Reference `RazorBlade` directly in the application.** RazorBlade ships `build/` but no
`buildTransitive/`, and MSBuild props do not flow transitively. Referencing only
`Hardened.Templates.RazorBlade` means the `.props` that globs `**/*.cshtml` never reaches your
project, and your views compile to nothing with no error.

```xml
<PackageReference Include="RazorBlade" Version="1.0.0" />
<PackageReference Include="Hardened.Templates.RazorBlade" Version="..." />
```

**Kestrel and ASP.NET Core hosts need one Razor generator, not two.** RazorBlade emits warning
RB0006 when `UsingMicrosoftNETSdkRazor` is true, because its generator and the SDK's would both
process the same `.cshtml`. Set `EnableDefaultRazorBladeItems=false` and list views explicitly, or
keep them out of the Razor SDK's default globs.

## Naming

If your own assembly's namespace ends in `RazorBlade`, qualify the base type in `.cshtml` with
`global::`. Without it the name binds to the enclosing namespace and fails to resolve.

## Writing a base of your own

`HardenedHtmlTemplate<TModel>` inherits RazorBlade's **non-generic** `HtmlTemplate` and declares its
own `Model`. Both of the natural alternatives fail:

- `RazorBlade.HtmlTemplate<TModel>.Model` is read-only (`CS0200`), so a model cannot be attached
  after construction.
- RazorBlade emits the `(TModel model) : base(model)` constructor **only for its own base types**, so
  a custom generic base gets parameterless construction and fails with `CS7036`.
