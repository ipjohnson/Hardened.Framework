# Filters at the entry point and the assembly

A filter can be declared on a handler method and on its controller class. It cannot be declared for
a whole application without leaving the compilation, and the route that does leave it —
`[Enable<T>]` and a DI-registered global provider — is invisible to the document generator. This
note is about closing that: four rungs walked for every handler, the wide two collected once, and
one merged metadata list that everything downstream reads.

## What happens today

Three separate mechanisms put a filter in a chain.

**Attributes on a handler.** `BaseRequestModelGenerator.GetFilters` collects attributes implementing
`IRequestFilterProvider` from the method and from its containing class — two rungs, no more.
`HandlerInfoCodeGenerator` writes them into a `_metadata` array per generated handler class, method
first, and `ExecutionHelper.GetFilterInfo(_metadata)` hands the ones that are filter providers to
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

## The four-rung walk already exists

`TimeoutResolver` resolves a deadline through the operation, its class, the handler's assembly and
the entry point's default, and its remarks settle two things this design needs.

**Most specific wins, and nothing is combined.** *"Unlike a requirement, two budgets do not compose
into a third, so the nearest declaration to the handler is the answer and the rest are fallbacks."*
The contrast is the point: `RequirementFrom(Metadata)` conjoins every authorization attribute it
finds, so the framework already has both policies side by side, plus a third — a timeout convention
may only tighten.

**The assembly beats the entry point.** *"A `[WebLibrary]` project writing `[assembly:
Timeout(Milliseconds = 2000)]` is saying something specific about its own handlers; an entry point
writing `[Enable<RequestTimeouts>]` is stating a blanket fallback for handlers that said nothing.
Read the other way round, a host would silently loosen a bound a library deliberately set."* That
ordering is the shared walk's ordering, not one resolver's private rule.

So the walk is not new. What is new is doing it once, for filters as well as budgets, and building
one list everything reads.

## The proposal

**Walk all four rungs for every handler, merge them into `Metadata`, and collect the wide two once.**

Uniqueness lands at two levels, in different places, and separating them is most of the design.

### Between the assembly and the entry point — at build time, once

The generator has both lists as `AttributeModel`s and already keys on `TypeDefinition`, which is
what `HandlerInfoCodeGenerator` constructs the instances from. Emit the assembly's items first, then
only the entry-point items whose closed type is not already present, into one array beside the
routing table — the way `ServerSentEventManifestEmitter` already puts a nested class there:

```csharp
public partial class Application {
    private static class ApplicationFilters {
        // assembly rung, then entry-point items whose type is not already here
        internal static readonly object[] Wider =
            new object[] { new AuditAttribute { Category = "quotes" }, new ConditionalGetAttribute() };
    }
}
```

Unique before it ships, at no run-time cost, and one place where the arguments are spelled.

If the generator emits the assembly rung as well as the entry point's, there is no reflection
anywhere: the generator's compilation *is* the handler's assembly, so `GetCustomAttributes()` leaves
the startup path entirely and `TimeoutResolver`'s per-assembly `ConcurrentDictionary` goes with it.
That is also the AOT-friendly shape.

### Against the handler's own two rungs — per handler, as the chain is built

This is the part no shared array can precompute, because the answer differs per handler. One pass,
once per handler, not per request. `CreateFilterArray` already replaces `handlerInfo` twice on that
line — `ApplyConventions`, then `WithTimeout` — so a third sits in the same seam:

```csharp
handlerInfo = handlerInfo.WithWiderRungs(ApplicationFilters.Wider);
// method + class, as emitted; then each wider item whose closed type is absent,
// unless its AttributeUsage says AllowMultiple
```

**The key is the closed attribute type, and `AttributeUsage.AllowMultiple` decides replace against
compose.** `false` means nearest wins and the wider declaration is dropped; `true` means every rung
contributes. The author already had to declare it in C#, so this needs no new vocabulary, and it is
cacheable per attribute type. It also falls out correctly for generics: `Authorize<BearerAuth>` and
`Authorize<PetsOAuth>` are different closed types and compose without special-casing.

### Merge into `Metadata`, not into a side list

If the walk produces `IExecutionRequestHandlerInfo.Metadata` itself, every existing reader inherits
the four-rung view: `RequirementFrom(Metadata)`, `TimeoutFrom`, `StreamsResponse`, and
`ConditionalGetAttribute.Declares` — which then stops needing to exist, because the merge has
already applied the rule it was hand-rolling.

One thing to get right: **the suppression has to be applied while the list is built, not after.**
Merge first and filter later and `Declares` sees the module's own instance, and the module stands
itself down.

## From "apply here" to "apply where applicable"

A declaration's meaning changes with its rung. On a method it says *install on this handler*. On the
entry point or the assembly it says *install where you apply*, and applicability stops being a
footnote and becomes the whole content of the declaration.

`IRequestFilterProvider.GetFilters(handlerInfo)` already expresses both — it is handed the handler
and returns zero or more filters, so `yield break` is "not here". `ConditionalGetAttribute` already
does it, because a class-level declaration on a controller that also writes had to install nothing
on the writes.

What is wrong is that the rule is stated **six times** on that one attribute: `Methods = Reads,
NotWhenStreaming = true` on each of five facets, plus the `if` at the top of `GetFilters`. The
`Reads` constant exists to stop it being spelled five times, and its own comment says why — *"a
document claiming a 304 on an operation the filter stood down on is the defect the declarations were
added to close."*

**So hoist applicability to one declaration on the attribute type, and have both halves read it.**
The provider stops re-implementing the rule; the facets stop repeating it; the document generator
reads the same statement the pipeline obeys. Two diagnostics then fall out, and both are the defect
class this whole trial kept finding — a declaration that reads as a commitment and does nothing:

- **A filter attribute at a wide rung that declares no applicability means every handler.** That
  should be something an author wrote deliberately, not a default they backed into. `[Retry]` on an
  entry point retrying every write is the shape to worry about.
- **A filter at a nearer rung on a handler it does not apply to installs nothing, silently.**
  `[ConditionalGet]` on a POST today. It should name both the declaration and the handler and stop
  the build, the way `HRDT001` does for a thrown type that declares no status.

That is also the honest boundary on "one standard method": the *walk* and the *dedup* are standard —
four rungs, closed-type keys, `AllowMultiple` — while applicability is per-attribute data, declared
once and read by two halves. Nothing about a rung can infer whether a filter belongs on a POST.

## What this settles, and what it changes

`[ConditionalGet]` on a module class installs on every GET in that compilation and publishes the
304, the `ETag` on both statuses and both conditional request headers — from one declaration,
because `FilterResponseSelector.Declarations` gains the same entry-point rung and the facets follow
the attribute wherever it is written. The applicability conditions are already in metadata a
generator can read: `Methods = "GET,HEAD", NotWhenStreaming = true`.

The behaviour change that needs its own test rather than arriving as a side effect: once `Metadata`
is the merged list, `[assembly: AuthorizeGrants("admin")]` starts composing into the requirement of
every handler in that assembly. That is very likely what whoever writes it expects — it is what
`RequirementFrom` does with the rungs it can already see — but it is a change, not a refactor.

**Ties still break on insertion order.** `CreateFilterArray` sorts by `RequestFilterInfo.Order` and
breaks ties on insertion: registry first, then timeout, IO, invoke, instance, then the handler's own
providers. A wider-rung filter at the same order as a handler's own therefore runs first. That is
probably right — the wider declaration was there first — but it is a decision, and it should be
stated rather than discovered.

**Equality still matters at the registration site.** `ConditionalGet` overrides `Equals` and
`GetHashCode` so that enabling it twice registers one default. The build-time dedup above covers the
two wide rungs; a module registered twice through DI is still the module's problem.

## What `[Enable<T>]` keeps

It is not replaced. It earns its place for two things a compile-time rung cannot do:

- **Registering services.** A `[DependencyModule]` can bring a store, options, a hosted service.
  Response caching and rate limiting need that. `ConditionalGet` does not: its whole
  `ConfigureServices` is one `AddGlobalFilter` call, which is why it is the feature that shows the
  gap most plainly.
- **A host switching on a library's handlers.** Only a startup registration reaches handlers
  compiled in an assembly the host merely references.

### The limit that stays

`[Enable<ConditionalGet>]` on a **host** entry point can never publish into a **library's**
document. They are separate compilations, and the template says as much twice: once about
`[ApiGatewayModule]`, once about why `[Enable<OpenApiDocumentPublishing>]` belongs on the library
module and emits `"paths": {}` on the host. That is the argument for making the library-module and
assembly rungs the ones an application reaches for, and leaving the host flag as the deployment
switch it is.

## Specification-first

The same rungs apply. A described operation's filters come from `x-filters` in the contract and
reach the model through `RequestModelBuilder`'s `WithFilters`; an entry-point or assembly
declaration is orthogonal and merges the same way. `AGENTS.md` says a spec-first application has
nowhere to put a route attribute — this is one of the few it *can* put somewhere, because the module
class is ordinary C# in the same compilation.

## Migration

Nothing breaks. `[Enable<ConditionalGet>]` keeps working and keeps installing the same filter;
handlers with their own `[ConditionalGet]` keep their own. What is new is that

```csharp
[HardenedModule]
[HardenedWebModule]
[ConditionalGet]
public partial class TodoLibrary { }
```

installs on every GET in that library *and* says so in the document.
`ConditionalGetAttribute`'s `AttributeUsage` gains `Assembly` if the assembly rung is wanted for it
specifically; the collection change is what makes any filter attribute work there.

## Open questions

1. **Does a wider rung reach a referenced library's handlers?** Compile-time collection cannot cross
   that seam; a DI registration does it automatically. Two different answers, and only one can be
   the default. The walk above chooses compile-time, which means a host's declaration covers the
   host's own handlers — the same answer `TimeoutResolver` already gives for `[assembly: Timeout]`.
2. **Does `[Enable<T>]`'s type argument also contribute facets to the document?** Cheap where the
   entry point and the handlers share a compilation, impossible where they do not — which argues for
   documenting the limit rather than half-closing it.
3. **Which existing global filters move to a rung.** `[Enable<HardenedCompression>]` is next with the
   same shape.
4. **Does `TimeoutResolver` fold into the shared walk in the same change, or after it?** Its assembly
   rung and cache are exactly what the walk subsumes, but it also carries the tighten-only convention
   pass, which nothing else has.
