# Hardened.Templates.RazorBlade

Renders Razor views for handlers annotated with `[Template<T>]`.

RazorBlade compiles `.cshtml` to C# at build time and has no dependency on ASP.NET Core — no
`Microsoft.AspNetCore.App` framework reference, no MVC types in the generated code. That is what
makes it usable from the Lambda runtimes, which are plain `Microsoft.NET.Sdk` projects.

## Wiring

Turn it on by naming the marker, which is what references the package:

```csharp
[HardenedModule]
[HardenedWebModule]
[KestrelRuntime]
[Enable<HardenedRazorTemplate>]
public partial class Application { }
```

That generates `ApplicationRazorTemplate<TModel>` — the entry point's name plus the marker's — for
views to inherit:

```razor
@inherits Application.ApplicationRazorTemplate<FortunePage>
<table>@foreach (var fortune in Model.Fortunes) { <tr><td>@fortune.Message</td></tr> }</table>
```

and a handler names the view by type:

```csharp
[Get("/fortunes")]
[Template<Views.Fortunes>]
public FortunePage GetFortunes() => _repository.Load();
```

There is nothing to register. The generated handler puts a factory on the response,
`TemplateResponseSerializer` builds the view, hands it the model and asks it to render. A template
renders itself — there is no engine, and no name resolved through a dictionary.

## What the compiler checks, and where

The boundary is not where it looks.

**On the attribute.** `TemplateAttribute<T>` constrains `T` to `IHardenedTemplate, new()`, and the
attribute is applied in your own assembly, where RazorBlade's output exists. A type that is not a
template, or has no parameterless constructor, is an error on the attribute — naming the template.

This is also why the view is named by type rather than by string: because the attribute is in your
assembly, RazorBlade's `internal` generated classes are nameable there. A registry of named
descriptors existed to work around exactly that.

**In generated code.** That the view's model matches the handler's return type cannot be expressed
on the attribute — it does not know the return type — and the generator cannot check it, because
the view is another generator's output and invisible to it. So the generator emits an assignment
the compiler has to bind:

```csharp
private static readonly IHardenedTemplate<FortunePage> _templateCheck_GetFortunes = new Views.Fortunes();
```

A mismatch reads "cannot convert Views.Fortunes to IHardenedTemplate<FortunePage>", naming both
types. That is the one mechanism that works across a generator boundary: another generator's output
cannot be inspected, but code can be emitted that the compiler binds against it.

## Choosing a view per request

The response carries a factory, assigned before the handler runs, so a handler or filter can
replace it — a different view for mobile than for desktop, an A/B test, an error view:

```csharp
context.Response.TemplateFactory = static _ => new Views.FortunesMobile();
```

One construction shape only. C# has `where T : new()` but no constraint for "has a constructor
taking `TModel`", so a second shape could not be compile-checked and would not deliver the guarantee
that makes this worth having. The model is attached after construction.

## Specification-first

The same attribute works on the implementation of a spec-generated service interface. The document
declares the contract — that the operation answers with `text/html` — and the attribute names the
view that produces it, so changing engines or views does not edit the API description.

There is no spec extension for this. Which server-side view renders a response is not part of an
HTTP contract: clients, gateways and documentation tooling have no views, and a second
implementation of the same specification in another language has nothing to do with the value.

## Content type

From the marker, which is to say from the base class:

| Marker | Base | Content type |
|---|---|---|
| `HardenedRazorTemplate` | `HardenedHtmlTemplate<T>` | `text/html; charset=utf-8` |

A handler that sets `Response.ContentType` itself keeps it — rendering only fills in a blank.

Another engine ships its own marker with its own `[TemplateBase]` and `[TemplateContentType]` and
needs no change to Hardened: the generator resolves whichever marker was named and reads those two
attributes. Two markers on one module produce two bases, so multi-engine is the same mechanism
rather than a retrofit.

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

If your own assembly's namespace ends in `RazorBlade`, qualify the base type in `.cshtml` with
`global::`. Without it the name binds to the enclosing namespace and fails to resolve.

## Why the base is what it is

`HardenedHtmlTemplate<TModel>` inherits RazorBlade's **non-generic** `HtmlTemplate` and declares its
own `Model`. Both of the natural alternatives were tried and neither works:

- `RazorBlade.HtmlTemplate<TModel>.Model` is read-only (`CS0200`), so a model cannot be attached
  after construction — and attaching after construction is the whole shape of this design.
- RazorBlade emits the `(TModel model) : base(model)` constructor **only for its own base types**, so
  a custom generic base gets parameterless construction and fails with `CS7036`.

Inheriting the non-generic base sidesteps both: parameterless construction is correct, and there is
no constructor for anyone to generate.
