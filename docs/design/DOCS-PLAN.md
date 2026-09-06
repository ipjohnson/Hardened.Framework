# Documentation plan

Written after a first-adopter run against 0.8.0-rc1000 that produced eleven findings, nine of
them documentation rather than code. This is a plan for the docs, the README, and the structure
underneath both.

## The thing not to break

The guide pages are genuinely good — better than most commercial framework documentation. They
explain *why*, they own their mistakes in dated "Changed" notes, and `guide/routing` and
`guide/content-negotiation` are the kind of pages people bookmark. `Hardened.Web.Kestrel.Runtime`'s
README, which measures its own benefit and lists what you give up, is the best single document in
the project.

None of that is the problem, and a rewrite would destroy the main asset. **The docs are excellent
essays and a poor map.** Everything below is about the map.

---

## Diagnosis

### The structural cause: the docs are in a different repository

`ipjohnson/Hardened.Docs` is a separate repo from the code it describes. Every failure below
follows from that one fact:

- Snippets cannot be compiled, because there is no compilation to compile them against.
- The package table is hand-maintained, because nothing can generate it from the build.
- A new package ships with no page, because no gate in this repo knows the docs exist.
- `Hardened.Web.Kestrel.Runtime/README.md` — the document that would have prevented three of my
  eleven findings — never reached the site, because nothing carries an in-repo README there.

Three in-repo READMEs are invisible from the docs site today: Kestrel, RazorBlade, and Benchmarks.

### Four failure classes, with what each cost

**A — Prose that was never executed.** The quickstart is missing two `PackageReference` lines and
says the generators "arrive with those packages"; it does not compile. The `guide/templates`
callout recommends `EnableDefaultRazorBladeItems=false`, which I tested and which does not fix
what it claims to. `guide/execution-pipeline` documents a filter keyed on `handlerInfo.Path`, which
could not match a route under a module `[BasePath]` — a real defect, now fixed, that a compiled
sample would have caught years earlier than a stranger did. Pipeline snippets omit their `using`
lines; I had to grep the source for `IRequestFilterProvider`'s namespace.

**B — Hand-maintained inventories that drift.** Six published packages are absent from
`reference/packages`, including the recommended host. One listed package,
`Hardened.DependencyModules.SourceGenerator`, is stuck at `0.1.0-rc1` on nuget.org and is a dead
end. The installation page still routes people through GitHub Packages and a token; the same
reference page contradicts itself about the feed within one screen. `Hardened.Templates.RazorBlade`
is pinned four release lines stale in its own install snippet.

**C — Shipped features with no page.** Kestrel hosting, static content, and validation all ship at
0.8.0 and have no page. Validation works well and is nearly invisible: DataAnnotations plus one
generator package, and the emitted validator is clean straight-line code.

**D — No blessed path.** Nothing in the docs says which host to use. `guide/getting-started`, the
repo README and `guide/routing` all lead with `[AspNetCoreRuntime]`, so that is what I built on —
into a Razor SDK conflict and an `IStartupService` that never runs. Both evaporate on Kestrel.
`guide/templates` and `guide/routing` do use `[KestrelRuntime]` in examples, without any page
naming the package it comes from.

### What an adopter actually does

I read `guide/getting-started`, then reached for pages by name when stuck. I never opened
`reference/packages` before my first build — which is where the two missing package references
and the Kestrel package were both documented. That is not unusual behaviour and the structure
should not depend on it: **anything a reader needs before their first successful build has to be
on the first page they open.**

---

## Plan

Five workstreams. The first two remove whole failure classes rather than fixing instances; the
rest are cheaper and can run in any order after.

### 1. A template, and make it the getting-started page

`dotnet new hardened-web` — Kestrel, nuget.org, the two generator packages, one controller, one
test. No such template is published today.

The page then becomes four lines and a `curl`, and cannot drift, because the template is a project
CI builds and runs. This alone removes findings 1, 2, 8 and 11 from my report and most of failure
class A at the point where it does the most damage.

`hardened-adopter/starter/` in the audit workspace is a working version of exactly this — four
files, verified serving. Use it as the template body.

Ship `hardened-console`, `hardened-lambda` and `hardened-library` behind the same door once the
first one exists.

### 2. Move the docs source into this repository, and compile every snippet

The docs site can keep its own repo for theme and hosting, but **the prose and its code should
live beside the code they describe.**

Then the thing that matters becomes possible: no code in the docs that is not compiled.

- `docs/samples/*` — real projects, in the solution, built and tested by CI.
- Markdown includes named regions from those files rather than containing code:
  `<<< @/samples/greeter/Program.cs#registration` (VitePress supports this natively today).
- A lint step fails the build on a fenced `csharp` or `xml` block in `docs/` that is not an
  include. Exceptions get an explicit marker so they are countable.

Everything in class A becomes a build failure instead of a reader's afternoon. The
`handlerInfo.Path` defect specifically: a sample exercising the documented filter pattern would
have failed the moment the pattern stopped working.

Cost is real — this is the expensive item — but it is the only one that stops class A recurring.

### 3. Generate the inventories

`reference/packages` should be generated from the pack output, not typed. Two CI gates, in the
style of the existing `scripts/coverage-gate.py`:

- **Every packable project appears in the generated table.** 21 projects are packable today; six
  were missing from the table.
- **Every version named in docs resolves on nuget.org.** Catches the stale RazorBlade pin and the
  dead `Hardened.DependencyModules.SourceGenerator` entry.

Then delete the GitHub Packages installation section and replace it with a "continuous builds"
aside, and remove the self-contradiction on the reference page.

### 4. A docs-coverage gate

Every packable project must name a docs page, or carry an explicit exemption with a reason. Fails
CI when a new package appears with neither. This is the check that would have caught Kestrel,
static content and validation shipping into silence.

Pair it with a one-time sweep: promote the three in-repo READMEs to real pages. The Kestrel one is
close to publishable as written.

### 5. Add a map layer; leave the essays alone

Three new pages, and no rewrites:

- **Start here.** The template, and nothing else.
- **Choosing a host.** The missing page. Kestrel is the default and why; ASP.NET Core when you need
  its middleware, authentication, or hosting diagnostics; Lambda for Lambda. The Kestrel README
  already contains most of this, including the measurements and the honest "what you give up"
  list — it just needs to be somewhere a reader will find it before choosing.
- **How do I…** — a task index over the existing pages. Serve HTML. Read configuration. Add a
  filter. Split into libraries. Validate a body. Serve static files.

Then one editing pass over the existing pages to make Kestrel the default in every code sample,
with ASP.NET Core shown as the alternative rather than the norm.

---

## Specific edits, independent of the above

Small, and each one cost me time:

| Page | Change |
|---|---|
| `guide/getting-started` | The two generator packages; delete the claim that they arrive on their own; nuget.org not GitHub Packages; Kestrel |
| `reference/packages` | Generate it; fix the feed contradiction; `OutputItemType="Analyzer"` is `ProjectReference` syntax shown on a `PackageReference` |
| `guide/modules` | How a service reads its own module's configured property — the `Tenant` example sets one and nothing shows reading it back. Note that module equality is by type, so two imports with different settings collapse unless the module overrides `Equals` (and cross-link DependencyModules) |
| `guide/execution-pipeline` | `using` lines on every snippet; state that a response header must be set before the serializer runs, and that Hardened drops a late one silently where ASP.NET Core throws |
| `guide/testing` | Say xUnit **v3**, and that v3 test projects need `<OutputType>Exe</OutputType>` |
| `guide/templates` | Fix or delete the `EnableDefaultRazorBladeItems` workaround — it does not work; the answer is to not use the Web SDK. Update the stale `0.4.0-rc1000` pin |
| `guide/routing` | `[Get]`'s `SuccessStatus`/`ErrorStatus` properties are already documented as ignored — good; keep that honesty when the map layer lands |
| README | Same quick start as the template, and one line on choosing a host |

Also worth a line somewhere: `[HardenedStaticContent]` has no `RoutePrefix` although the MSBuild
item does, so the attribute and the item disagree about what is configurable.

---

## Re-running the adopter test

The point of all this is that the next stranger gets further. That is measurable, and it should be
run against a build rather than a memory.

The audit workspace at `hardened-adopter/` is the harness:

- `starter/` — the corrected quickstart, four files.
- `Bookshelf/` — a two-project application with 14 tests, exercising modules, constraints, every
  binding source, filters, configuration, validation, views, static content and links.
- `repro-docs-quickstart/`, `repro-module-properties/` — minimal repros.

Re-run it cold after the template lands: fresh workspace, published packages only, docs only, no
source. The number worth tracking is **how long until the first successful `curl`**, and **how many
times the reader has to open the source tree**. Mine were roughly thirty minutes and eight.

Two of the three code defects that run found were invisible to a 4,000-test suite because every
test used a double that could not fail — a `MemoryStream` that accepts synchronous writes, and a
handler path nothing compared against a served URL. That is the argument for the sample projects in
workstream 2 more than any doc-quality argument is.

---

## Order

1. **Template** — biggest reduction in time-to-first-curl for the least work.
2. **Docs into the repo, snippets compiled** — expensive, and the only thing that stops class A
   coming back.
3. **Generated package table + version gate** — small, mechanical, ends class B.
4. **Docs-coverage gate + promote the three READMEs** — ends class C.
5. **Map layer and the Kestrel default pass** — ends class D.
6. **The specific edits table** — do opportunistically; none blocks anything else.
