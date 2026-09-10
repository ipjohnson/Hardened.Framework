# Filters at the entry point and the assembly

A filter can be declared on a handler method and on its controller class. It cannot be declared for
a whole application without leaving the compilation, and the route that does leave it —
`[Enable<T>]` and a DI-registered global provider — is invisible to the document generator. This
note is about closing that: two more rungs, collected once, emitted once, and read by both halves.

## What happens today

Three separate mechanisms put a filter in a chain.

**Attributes on a handler.** `BaseRequestModelGenerator.GetFilters` collects attributes implementing
`IRequestFilterProvider` from the method and from its containing class — two rungs, no more.
`HandlerInfoCodeGenerator` writes them into a `_metadata` array per generated handler class, and
`ExecutionHelper.GetFilterInfo(_metadata)` hands the ones that are filter providers to
`CreateFilterArray`:

```csharp
private static readonly object[] _metadata = new object[] { new ConditionalGetAttribute() };
```

The array is also what `IExecutionRequestHandlerInfo.Metadata` exposes, so a filter can ask what
else was declared on the handler it is being installed on.

**The global registry.** `IGlobalFilterRegistry` holds every `IRequestFilterProvider` registered as
a service, and `CreateFilterArray` asks it for filters for each handler as the chain is built.
`AddGlobalFilter(provider, when:)` wraps the provider in a `ConditionalFilterProvider` whose
predicate is read once per handler.

**`[Enable<T>]`.** A `[DependencyModule]` whose `ConfigureServices` calls `AddGlobalFilter`.
`ConditionalGet` is exactly this and nothing more:

```csharp
services.AddGlobalFilter(
    new ConditionalGetAttribute(),
    when: handlerInfo =>
        !ConditionalGetAttribute.Declares(handlerInfo) && !handlerInfo.StreamsResponse);
```

The document is written from a fourth thing: `FilterResponseSelector.Declarations` scans the
method, its class, and the assembly, and reads the `[AnswersStatus]` / `[AnswersHeader]` /
`[ReadsHeader]` facets off whatever it finds.

## What that costs

The rungs do not line up. Filters install from two, facets are read from three, and neither reads
the entry point — so:

- `[ConditionalGet]` on a module class compiles (its `AttributeUsage` allows `Class`) and installs
  nothing.
- `[assembly: ConditionalGet]` does not compile at all; the usage is `Method | Class`. The
  document side would have read it.
- `[Enable<ConditionalGet>]` installs the filter on every GET and publishes it on none. That is the
  0.32 trial's A-04: four GETs answer 304 over the wire, one operation declares it in the document,
  and the template's own generated Kiota client throws `no error factory is registered for this
  code: 304` on exactly the request the feature exists to make cheap.

The runtime half of A-04 is not a defect — 304 everywhere is what the flag promises. The document
half cannot be fixed where it stands, because the predicate that decides which handlers are covered
is a `Func<IExecutionRequestHandlerInfo, bool>` evaluated at startup, and the document is written at
build time.

## The proposal

**Collect the entry point's and the assembly's filter declarations once, emit them once, and let
every handler in that compilation reference them.**

The entry point is already in hand where it is needed. `RoutingTableGenerator` runs on
`(EntryPointSelector.Model, ImmutableArray<RequestHandlerModel>)` — the module class and every
handler, combined, in one compilation — and `EntryPointSelector.Model.AttributeModels` is the same
list `[Server]`, `[OpenApiInfo]` and `[Enable<T>]` are read from. The assembly's attributes are in
that compilation too.

### Emit once, not per handler

The obvious implementation is to append the entry point's filters to each `RequestHandlerModel` and
let `_metadata` carry them. Do not do that. Three reasons, in order of weight:

1. **Incrementality.** A handler model is a Roslyn cache key. Folding the entry point's attributes
   into every handler makes one edit to the module class invalidate every handler in the
   application. `EnabledFeatureSelector`'s own remarks make this argument about feature detection;
   it applies here with more force, because handlers are the expensive half.
2. **One construction site.** `new AuditAttribute() { Category = "x" }` written into forty
   `_metadata` arrays is forty places for the arguments to be spelled, and forty copies in the
   assembly.
3. **It reads as what it is.** A declaration that covers the application should appear once in the
   generated source, next to the routing table it covers.

So: a nested static on the routing class, the way `ServerSentEventManifestEmitter` already puts one
there.

```csharp
public partial class Application {
    private static class ApplicationFilters {
        internal static readonly object[] Declared = new object[] { new ConditionalGetAttribute() };
    }
}
```

Every generated handler in the compilation gets that array as a second source alongside its own
`_metadata`. Two ways to wire it, and this is the first open question:

- **Through the handler constructor.** `GetFilterInfo(_metadata)` becomes
  `GetFilterInfo(_metadata, Application.ApplicationFilters.Declared)`. Explicit, but every handler
  names its entry point, which handlers currently do not do.
- **Through the registry, at startup.** The routing table registers `ApplicationFilters.Declared`
  into `IGlobalFilterRegistry` once. Nothing changes in the handlers at all, the composition seam
  already exists, and it stays a *compile-time declaration* even though it is applied at run time —
  which is the part that matters, because the document can then read the same declaration.

The second is smaller and keeps `CreateFilterArray` as it is. It also means the entry-point rung and
the `[Enable<T>]` rung end up in the same place at run time, which is where they belong.

### Rules that have to be written down

**Nearest rung wins.** `ConditionalGet` spells this today as
`!ConditionalGetAttribute.Declares(handlerInfo)` — a runtime scan of the handler's metadata for the
same attribute type. As a general rule it should not be each filter's job: a rung-declared provider
should stand down for a handler that declares the same provider type nearer. `Metadata` already
carries what the test needs. A filter that genuinely wants to install twice says so.

**Applicability.** `[ConditionalGet]` installs on GET and HEAD, and not on a streaming handler.
`GetFilters` enforces that at run time and the facets repeat it for the document —
`Methods = "GET,HEAD", NotWhenStreaming = true`. That pairing is the model to follow: the condition
belongs in metadata a generator can read, not only in a predicate.

**Order and ties.** `CreateFilterArray` sorts by `RequestFilterInfo.Order` and breaks ties on
insertion order: registry first, then timeout, IO, invoke, instance, then the handler's own
providers. If entry-point filters arrive through the registry they tie *ahead* of a handler's own
filters at the same order. That is probably right — the wider declaration was registered first — but
it is a decision, not an accident, and it should be stated.

**Equality.** `ConditionalGet` overrides `Equals`/`GetHashCode` so that enabling it twice registers
one default. Any rung-declared filter needs the same treatment or the same guarantee from the
collection site.

### The document reads the same rungs

`FilterResponseSelector.Declarations` gains the entry-point rung, so the facets follow the
declaration wherever it is written. The assembly rung is already there. After that, one declaration
on the module class both installs the filter and publishes the 304, the `ETag` on both statuses, and
`If-None-Match` / `If-Modified-Since` as parameters — on exactly the operations `Methods` and
`NotWhenStreaming` say it covers.

Note what this does *not* fix: `[Enable<ConditionalGet>]` on a **host** entry point still cannot
reach the document of a **library** compiled earlier. That is not a gap to close but a fact about
separate compilations — the template says the same thing twice, about `[ApiGatewayModule]` and
about why `[Enable<OpenApiDocumentPublishing>]` belongs on the library module. It is the argument
for making the library-module and assembly rungs the ones an application reaches for, and leaving
the host flag as the deployment switch it really is.

## What `[Enable<T>]` keeps

It is not replaced. It earns its place for two things a compile-time rung cannot do:

- **Registering services.** A `[DependencyModule]` can bring a store, options, a hosted service. A
  filter that needs any of those needs the module — response caching and rate limiting do.
  `ConditionalGet` does not: its whole `ConfigureServices` is one `AddGlobalFilter` call, which is
  why it is the feature that shows the gap most plainly.
- **A host switching on a library's handlers.** Only a startup-time registration reaches handlers
  compiled in an assembly the host merely references.

The convention in `docs/design/` — an attribute plus `[Enable<Feature>]`, with the default standing
down for an explicit declaration — stays. What changes is that the attribute gains two rungs, so the
common case (my application, my handlers, one declaration) no longer has to leave the compilation to
say "all of them".

## Specification-first

The same rungs apply. A described operation's filters come from `x-filters` in the contract and
reach the model through `RequestModelBuilder`'s `WithFilters`; an entry-point or assembly
declaration is orthogonal to that and composes the same way. `AGENTS.md` says a spec-first
application has nowhere to put a route attribute — this is one of the few attributes it *can* put
somewhere, because the module class is ordinary C# in the same compilation.

## Migration

Nothing breaks. `[Enable<ConditionalGet>]` keeps working and keeps installing the same filter;
handlers with their own `[ConditionalGet]` keep their own. What is new is that

```csharp
[HardenedModule]
[HardenedWebModule]
[ConditionalGet]
public partial class TodoLibrary { }
```

installs on every GET in that library *and* says so in the document. `ConditionalGetAttribute`'s
`AttributeUsage` gains `Assembly` if the assembly rung is wanted for it specifically; the collection
change is what makes any filter attribute work there.

## Open questions

1. Constructor argument or registry registration for the shared array — the two wirings above.
2. Should the entry-point rung be *inherited by imported library modules*? A host module composes
   library modules by attribute; a filter declared on the host today reaches only the host's own
   handlers. Compile-time inheritance across that seam is not possible; the registry route makes it
   automatic. Those are different answers and only one can be the default.
3. Does `[Enable<T>]`'s type argument also contribute facets to the document — i.e. is A-04's
   flag-on-the-host form worth publishing when the entry point and the handlers *are* in one
   compilation? It is cheap where they are and impossible where they are not, which argues for
   documenting the limit rather than half-closing it.
4. Which existing global filters move to a rung. `[Enable<HardenedCompression>]` is the next one
   with the same shape.
