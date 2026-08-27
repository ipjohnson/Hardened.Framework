# Views

::: tip Looking for `dotnet new`?
This page is about rendering HTML views with RazorBlade. The project templates are under
[Project templates](/guide/project-templates).
:::

A handler returns a model. A template turns that model into HTML. Neither knows about the other
until a request asks for `text/html` — so the same handler serialises as JSON for an API client and
renders a page for a browser, without a line of code about content types.

Rendering is done by [RazorBlade](https://github.com/ltrzesniewski/RazorBlade), which compiles
`.cshtml` files into C# classes at build time. A property that does not exist on the model is a
build failure, not a blank in the page.

## Why RazorBlade

Razor normally arrives with ASP.NET Core attached. The Razor SDK emits `RazorCompiledItemAttribute`,
three MVC namespace imports and five injected MVC properties into every compiled document, so
anything built on it needs a framework reference to `Microsoft.AspNetCore.App` — including a project
that never touches MVC. That rules it out for the Lambda runtimes, which are plain
`Microsoft.NET.Sdk` projects.

RazorBlade bundles the Razor parser inside its analyzer instead. The parser runs in the compiler and
never reaches your output, so a compiled view inherits from an ordinary base class and references
nothing but it. The same views work under Kestrel, ASP.NET Core and Lambda.

## Installing

Reference both packages:

```xml
<ItemGroup>
    <PackageReference Include="RazorBlade" Version="1.0.0" />
    <PackageReference Include="Hardened.Templates.RazorBlade" Version="0.14.0-rc1000" />
</ItemGroup>
```

Both, not just the Hardened one. RazorBlade ships no `buildTransitive/` folder, and MSBuild props do
not flow transitively — so referencing only `Hardened.Templates.RazorBlade` means the `.props` that
globs `**/*.cshtml` never reaches your project. Your views compile to nothing, with no error.

Then turn it on for a module:

```csharp
[HardenedModule]
[HardenedWebModule]
[KestrelRuntime]
[Enable<HardenedRazorTemplates>]
public partial class Application { }
```

`[Enable<T>]` is the framework's one name for every optional generated feature — type `[Enable<` and
let completion list what the project has referenced. Naming the marker is also what references the
package, so there is nothing to detect: a generator that probed for a type would be guessing, and
the compiler is not.

That generates `ApplicationRazorTemplates<TModel>` — the entry point's name plus the marker's — which
is what your views inherit.

::: warning ASP.NET Core hosts
RazorBlade warns with `RB0006` when a project also uses the Razor SDK, because both generators would
process the same `.cshtml` files. Set `EnableDefaultRazorBladeItems=false` and list your views
explicitly, or keep them out of the Razor SDK's default globs.
:::

## Writing a view

```razor
@* Views/Orders.cshtml *@
@using Contoso.Orders.Models
@inherits Contoso.Orders.ApplicationRazorTemplates<OrderListModel>

<table>
    <tr><th>Reference</th><th>Total</th><th>Placed</th></tr>
    @foreach (var order in Model.Orders)
    {
        <tr>
            <td>@order.Reference</td>
            <td>@order.Total.ToString("###.00")</td>
            <td>@order.PlacedAt</td>
        </tr>
    }
</table>
```

It is Razor, so `@foreach`, `@if` and `@(...)` all work, and `@order.Reference` is HTML-encoded.
`@Html.Raw(value)` opts out when you mean to emit markup.

The generated base decides what the view produces:

| Marker | Base | Content type | Encoding |
|---|---|---|---|
| `HardenedRazorTemplates` | `HardenedHtmlTemplate<T>` | `text/html; charset=utf-8` | HTML-encoded |

The content type comes from the marker rather than the file extension, so a `.cshtml` file can
legitimately produce something other than HTML — with a marker whose base escapes accordingly.

Two markers on one module produce two bases, `ApplicationRazorTemplates<T>` and
`ApplicationFluidTemplates<T>`, so multi-engine is the same mechanism rather than a retrofit.

### Why the base looks the way it does

`HardenedHtmlTemplate<TModel>` inherits RazorBlade's **non-generic** `HtmlTemplate` and declares its
own `Model`. Both of the natural alternatives were tried and neither works:

- `RazorBlade.HtmlTemplate<TModel>.Model` is read-only (`CS0200`), so a model cannot be attached
  after construction — and attaching after construction is the whole shape of this design.
- RazorBlade emits the `(TModel model) : base(model)` constructor **only for its own base types**, so
  a custom generic base gets parameterless construction and fails with `CS7036`.

Worth knowing before writing a base of your own.

## Naming a view from a handler

`[Output<T>]` says which view writes the response:

```csharp
using Hardened.Requests.Abstract.Attributes;

public class OrderController {
    [Get("/orders")]
    [Output<Views.Orders>]
    public OrderListModel List() => _orders.Recent();
}
```

A type, not a name. Because the attribute is applied in your own assembly, RazorBlade's `internal`
generated view classes are nameable there — which is the exact problem a registry of named
descriptors existed to work around. There is nothing to register: the generated handler puts a
factory on the response and the view renders itself.

It works the same way on the implementation of a
[generated OpenAPI service interface](/guide/openapi):

```csharp
[Handler]
public class OrderServiceImpl : IOrderService {
    [Output<Views.Orders>]
    public Task<OrderListModel> ListOrders() => _orders.RecentAsync();
}
```

That is where it belongs for a spec-first application. The document declares that the operation
answers `text/html`; which view produces that HTML is how your implementation fulfils it, so changing
views or engines does not edit your API description. There is no spec extension for it — which
server-side view renders a response is not part of an HTTP contract, and a second implementation of
the same specification in another language has nothing to do with the value. A document that
promises `text/html` for a model and an implementation that names no view is a build error.

## What the compiler checks, and where

The boundary is not where you would guess.

**On the attribute.** `OutputAttribute<T>` constrains `T` to `IHardenedResponseOutput, new()`, and
it binds in the final compilation where RazorBlade's output exists. A type that is not an output, or
has no parameterless constructor, is an error on the attribute — naming the type.

**In generated code.** That the view's model matches the handler's return type cannot be expressed on
the attribute, which does not know the return type, and the generator cannot check it, because the
view is another generator's output and invisible to it. So the generator emits an assignment the
compiler has to bind:

```csharp
private static readonly IHardenedResponseOutput<OrderListModel> _outputCheck_List = new Views.Orders();
```

A mismatch reads `cannot convert Views.Orders to IHardenedResponseOutput<OrderListModel>`, naming both
types. That is the one mechanism that works across a generator boundary — another generator's output
cannot be inspected, but code can be emitted that the compiler binds against it. It is the same
property that makes a route change break a `.cshtml` at build time.

## Choosing a view per request

The response carries a factory, assigned before the handler runs, so a handler or a filter can
replace it — a different view for mobile than for desktop, an A/B test, an error view:

```csharp
context.Response.OutputFactory = static _ => new Views.OrdersMobile();
```

One construction shape only. C# has `where T : new()` but no constraint for "has a constructor taking
`TModel`", so a second shape could not be compile-checked and would not deliver the guarantee that
makes this worth having. The model is attached afterwards.

## Links in a view

A view built on a generated base has a `Links` property, which is where
[generated links](/guide/routing#links-to-your-own-routes) pay off:

```razor
<a href="@Links.Orders.Order(order.Id)">@order.Reference</a>
```

RazorBlade copies `@` expressions verbatim and emits `#line` directives with exact spans, so renaming
the route or its handler breaks the template at build time, reported at its own line and column.
Rails' `product_path` and Flask's `url_for` are runtime lookups: the same mistake fails when someone
loads the page.

## What actually gets rendered

**Declaring an output takes the response out of negotiation.** The view either answers what the
client asked for, or the request gets `406 Not Acceptable`:

| Request | Response |
|---|---|
| `Accept: text/html` | The rendered view |
| `Accept: */*`, or no header | The rendered view |
| `Accept: application/json` | `406`, with no body |

That is a data-leak rule rather than a status-code preference. A view usually renders a subset of
what its model holds — a page showing a customer's name, from a model carrying their address, their
billing details and every internal identifier attached to them. Serializing the model because the
client asked for JSON would put all of it on the wire, from a route whose author wrote nothing but a
view. So there is no fallback, and adding `[Output<T>]` to a handler can never widen what it
discloses.

To serve both representations from one handler, do not declare an output: return the model, and let
[content negotiation](/guide/content-negotiation) choose a serializer. A route that must answer both
HTML and JSON is two routes or a filter that chooses, not one route quietly doing both.

## Layouts, sections and partials

These are RazorBlade's rather than Hardened's, so its
[documentation](https://github.com/ltrzesniewski/RazorBlade) is the reference. In outline: a layout
is a view deriving from `HtmlLayout`, a view opts into one with
`@implements IUsesLayout<Views.Layout>`, and `RenderPartialAsync` composes views. Layout is a typed
relationship between two classes, not a filename convention.

## Writing another engine

There is no engine interface to implement. An output writes the response itself, so what another
engine ships is a marker and a base:

```csharp
[TemplateBase(typeof(FluidTemplate<>))]
[TemplateContentType("text/html; charset=utf-8")]
public sealed class HardenedFluidTemplate { }
```

The generator resolves whichever marker `[Enable<T>]` names, reads those two attributes and emits a
base deriving from what the first points at. It never learns what a marker *means* — a generator that
switched on the marker's name would need a change for every new engine, and the extensibility would
be fictional.

The base implements `IHardenedResponseOutput<TModel>`, which is two methods:

```csharp
bool SupportsContentType(string? accept, IExecutionContext context);
Task WriteOutput(IExecutionContext context);
```

The model is on `context.Response.ResponseValue`; the base reads it, casts once, and renders. A
template base also exposes `protected IExecutionContext Context`, which is what the generated
`Links` property resolves from — part of what `[TemplateBase]` declares rather than of the output
interface, since an output writing a file has no business carrying request state.

`typeof(X<>)` is legal in an attribute argument and an unbound generic as a type argument is not,
which is why the marker is a separate non-generic type pointing at the base rather than being the
base.

A marker may also be a DependencyModules module. `[Enable<T>]` requires `new()`, which is what a
module needs anyway, so a package shipping services alongside a generated type is one attribute
rather than two.
