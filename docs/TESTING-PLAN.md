# Hardened test coverage: assessment and workstream plan

**Date:** 2026-08-11
**Scope:** `Hardened.Framework` and `Hardened.Amz`. `Hardened.Canaries` is deliberately excluded —
see [Out of scope](#out-of-scope).

---

## 1. The short version

The framework is at **27.4% line / 27.0% branch**. The AWS repository is at **3.1% line / 3.8%
branch**, with twelve of its seventeen assemblies at exactly zero. Until Phase 0, CI measured
coverage on every build and enforced nothing, so both numbers could fall further and the build would
stay green.

More important than the numbers: **no test anywhere in either repository compiles the code the source
generators emit.** For a framework whose entire value proposition is "the wiring is written during
the build", that is the gap that matters. Every defect found in the last week was downstream of it.

---

## 2. How we got here

Four distinct defect classes, each from a real bug, each currently undetectable by the suite.

### 2.1 Generated code that does not compile

The parameter-binding integration suite surfaced three generator defects no unit test reached:

- named binding attributes emitted a double-quoted string literal
- handlers with metadata but no parameters put the metadata array in the parameters slot
- `[FromHeader]` called `Get` on a plain dictionary

All three emitted C# that failed to compile, so **any** project using those features failed to build.

The existing generator tests cannot catch this. `WebGeneratorCachingTests` asserts
`driver.GetRunResult().Diagnostics` is free of errors — but those are diagnostics *the generator
reported*, not compilation errors *in what it produced*. Nothing calls
`RunGeneratorsAndUpdateCompilation` and checks the resulting compilation.

```csharp
// What the tests assert today — passes even if the emitted C# is garbage
var diagnostics = driver.GetRunResult().Diagnostics
    .Where(d => d.Severity == DiagnosticSeverity.Error);
Assert.Empty(diagnostics);
```

The harness is already 90% of the way there: `GeneratorTestHarness.CreateCompilation` builds a real
`CSharpCompilation` with a full reference set. It just never compiles the output.

### 2.2 Public API that cannot be applied

`DeleteAttribute` and `PatchAttribute` shipped from the initial commit (2022-07-02) as:

```csharp
internal class DeleteAttribute { }
```

`internal`, and not derived from `Attribute`. No consuming project could apply them. The generator's
verb allowlist had included `Delete` and `Patch` the whole time, the runtime routed both, the README
and the NuGet package description both advertised `[Delete]` — and it went unnoticed for three years
because **nothing asserts that the shipped public surface is usable from a consumer's position.**

Fixed 2026-08-11, with integration coverage. The class of defect is not.

### 2.3 Declared behaviour that is never wired

`[Get]` and `[Put]` declare `SuccessStatus`, `NullReturnStatus`, `ValidationErrorStatus` and
`ErrorStatus`. The web generator reads none of them — `RequestHandlerNameModel` carries only path and
method, and the emitted `ExecutionRequestHandlerInfo` never sets the status overrides, so they fall
back to the interface's `null` defaults.

Four public properties, on two attributes, that compile and do nothing. No test asserts they have an
effect, so nothing failed when they stopped having one (or when they never had one).

### 2.4 Documentation drift

`Hardened.Web.Runtime`'s package description promised `[Delete]`. The README's package table promised
`[Delete]`. Both were false for three years. Nothing checks a claim in prose against the assembly.

### The common thread

Every one of these is **a gap between what the framework claims and what it does**, in a place no
test looks. Raising line coverage alone will not close them. The plan below adds coverage *and* the
three categories of assertion that would have caught each class: generated-code compilation, public
API surface snapshots, and behaviour-of-declared-options tests.

---

## 3. Where we are — measured

Numbers from a full `dotnet test --collect:"XPlat Code Coverage"` run merged with ReportGenerator,
2026-08-11, after Phase 0 landed. All tests passing: 588 framework, 120 AWS.

::: warning These supersede an earlier, wrong set of numbers
The first pass at this document aggregated raw coverlet output by taking the maximum across test
runs. That was wrong twice over: it double-counted lines covered by more than one run, inflating
both numerator and denominator, and it omitted every assembly no test loaded at all. ReportGenerator
merges properly. The corrected picture is worse, not better — AWS is at 3.1%, not the ~22% estimated.
:::

### 3.1 Hardened.Framework — 18 assemblies, **27.4% line / 27% branch** (6376/23228 lines)

| Assembly | Covered/total | Line | Branch |
|---|---:|---:|---:|
| `Hardened.Console.SourceGenerator` | 0/2926 | 0% | 0% |
| `Hardened.Requests.Serializers.Newtonsoft` | 0/17 | 0% | 0% |
| `Hardened.Templates.Abstract` | 0/21 | 0% | 0% |
| `Hardened.Templates.SourceGenerator` | 0/4000 | 0% | 0% |
| `Hardened.SourceGenerator` | 1305/4659 | 28% | 22.9% |
| `Hardened.Web.SourceGenerator` | 1640/4528 | 36.2% | 30.5% |
| `Hardened.Web.AspNetCore.Runtime` | 31/81 | 38.2% | 76.4% |
| `Hardened.OpenApi.SourceGenerator` | 1939/4720 | 41% | 38.2% |
| `Hardened.Commands` | 92/222 | 41.4% | 35.2% |
| `Hardened.Shared.Testing` | 92/187 | 49.1% | 27.1% |
| `Hardened.Templates.Runtime` | 149/297 | 50.1% | 51.2% |
| `Hardened.Shared.Runtime` | 147/231 | 63.6% | 69.9% |
| `Hardened.SourceGeneration.Testing` | 158/234 | 67.5% | 64.5% |
| `Hardened.Web.Runtime` | 115/165 | 69.6% | 70.3% |
| `Hardened.Requests.Runtime` | 436/618 | 70.5% | 68.5% |
| `Hardened.Requests.Abstract` | 64/89 | 71.9% | 78.2% |
| `Hardened.Requests.Testing` | 118/139 | 84.8% | 63.6% |
| `Hardened.Web.Testing` | 90/94 | 95.7% | 86.6% |

Generator assemblies dominate the denominator: `Hardened.Console.SourceGenerator` and
`Hardened.Templates.SourceGenerator` are thin wrapper projects, but each embeds the shared
generator library via linked `Compile` items, so each carries thousands of coverable lines at 0%.
That single fact accounts for most of the gap between the framework's per-assembly numbers — many
of them respectable — and its 27.4% total.

`Hardened.Shared.Testing` at 49.1% line / 27.1% branch is the one to worry about: it is the harness
every other suite is written with.

### 3.2 Hardened.Amz — 17 assemblies, **3.1% line / 3.8% branch** (333/10606 lines)

| Assembly | Covered/total | Line | Branch |
|---|---:|---:|---:|
| `Hardened.Amz.Cdk` | 0/253 | 0% | 0% |
| `Hardened.Amz.Function.DDB.Runtime` | 0/23 | 0% | 0% |
| `Hardened.Amz.Function.DDB.Testing` | 0/5 | 0% | — |
| `Hardened.Amz.Function.Lambda.SourceGenerator` | 0/4725 | 0% | 0% |
| `Hardened.Amz.Function.Lambda.Streaming` | 0/124 | 0% | 0% |
| `Hardened.Amz.Function.Lambda.Testing` | 0/28 | 0% | 0% |
| `Hardened.Amz.Function.Sqs.Runtime` | 0/19 | 0% | — |
| `Hardened.Amz.Function.Sqs.Testing` | 0/14 | 0% | 0% |
| `Hardened.Amz.Shared.Lambda.Testing` | 0/43 | 0% | 0% |
| `Hardened.Amz.Web.Lambda.Harness` | 0/14 | 0% | 0% |
| `Hardened.Amz.Web.Lambda.Runtime` | 0/127 | 0% | 0% |
| `Hardened.Amz.Web.Lambda.SourceGenerator` | 0/4668 | 0% | 0% |
| `Hardened.Amz.Function.Lambda.Runtime` | 5/74 | 6.7% | 20.8% |
| `Hardened.Amz.Shared.Lambda.Runtime` | 121/202 | 59.9% | 66.3% |
| `Hardened.Amz.DynamoDbClient.Testing` | 18/28 | 64.2% | 66.6% |
| `Hardened.Amz.Web.Lambda.Streaming` | 148/217 | 68.2% | 52.4% |
| `Hardened.Amz.DynamoDbClient` | 41/42 | 97.6% | 88.8% |

Twelve of seventeen assemblies are at zero. Before Phase 0 scaffolded a test project for each, they
did not appear in the coverage report at all — the repository looked like 59%, because only the five
assemblies that something happened to load were counted. They are now visible and gated.

The three that will hurt: `Hardened.Amz.Web.Lambda.Runtime` (API Gateway payload mapping — get it
wrong and every route 404s), `Hardened.Amz.Function.Sqs.Runtime` and
`Hardened.Amz.Function.DDB.Runtime` (partial batch responses — get them wrong and one poison message
redelivers the whole batch). All three run through `Hardened.Amz.Function.Lambda.Runtime`, at 6.7%.

### 3.3 What CI enforces

Both repositories run the tests with coverage, generate a ReportGenerator summary, and — since
Phase 0 — **fail the build when any assembly falls below its recorded baseline**
(`scripts/coverage-gate.py` against `coverage-baseline.json`). Before that, coverage was measured on
every build and enforced nothing: a PR deleting every test passed.

Also enforced, and worth keeping: `ContinuousIntegrationBuild=true` escalates warnings to errors; a
skipped test fails the build; and a test silently dropped for a duplicate ID fails the build.

## 4. Target and definition of done

| Gate | Before Phase 0 | Now | Target |
|---|---:|---:|---:|
| Framework line coverage | 27.4% | 27.4% | **75%** |
| Framework branch coverage | 27.0% | 27.0% | **65%** |
| AWS line coverage | 3.1% | 3.1% | **70%** |
| AWS branch coverage | 3.8% | 3.8% | **65%** |
| Shippable projects with no test project | 21 | **0** | 0 |
| Generators whose output is compiled in a test | 0 | 1 | **all 10** |
| Public API surface under snapshot | none | **13 assemblies** | all shipped |
| Coverage regression fails the build | no | **yes** | yes |

Coverage percentage is a proxy, and a poor one alone. The non-negotiable exits are the bottom four
rows — they are what close the four defect classes in §2.

---

## 5. Phase 0 — shared infrastructure (blocking, one agent)

**Everything in §6 depends on this. It must land first, and it must be done by a single agent,
because it touches files every other workstream would otherwise conflict over.**

### 0.1 `Hardened.SourceGeneration.Testing` — a shared generator-test library

New project, referenced by every generator test project. Must provide:

```csharp
// The assertion that closes §2.1. Runs the generator, updates the compilation,
// and fails with the emitted source alongside the compiler error.
GeneratorAssert.OutputCompiles(generator, source, references);

// Snapshot the emitted source so a change is visible in review.
GeneratorAssert.MatchesSnapshot(generator, source, snapshotName);

// The generator reported exactly these diagnostics and no others.
GeneratorAssert.Diagnostics(generator, source, "HOAG002");
```

Copied from `DependencyModules/tests/DependencyModules.Tests/Infrastructure/GeneratorTestHarness.cs`,
which already did all of this and more. Adapted for Hardened: reference anchors are caller-supplied
(there are ten generators, not one), `AdditionalFiles` support was added because OpenAPI specs and
templates reach their generators no other way, and failure messages print the offending generated
file with line numbers. The two repositories keep separate copies deliberately — a fix worth having
in one is usually worth porting to the other.

### 0.2 Public API surface snapshots

Add a `PublicApiGenerator`-style approved-surface test per shipped assembly:
`PublicApi.<Assembly>.approved.txt` checked in, test fails on any diff.

This is the cheapest possible fix for §2.2 — an attribute that is `internal`, or that stops deriving
from `Attribute`, shows up as a one-line diff in review. Prioritise
`Hardened.Web.Runtime`, `Hardened.Shared.Runtime`, `Hardened.Requests.Abstract`.

### 0.3 Scaffold every new test project, register it in the solution

**This is the critical step for parallel agent work.** Create every test project named in §6 — empty
but building, referenced correctly, added to `Hardened.Framework.sln` / `Hardened.Amz.sln`, with
`Bootstrap.cs` and `Usings.cs` in place.

`.sln` files, `Directory.Build.props` and `coverlet.runsettings` are shared. If ten agents each add a
project, every one conflicts. Doing it once up front means each agent afterwards only *adds files to
a directory it exclusively owns* — no shared-file edits, no merge conflicts.

### 0.4 Coverage gates in CI

Add per-assembly minimum thresholds to `coverlet.runsettings` and fail the build below them. Set each
threshold at **current measured coverage, rounded down** — a ratchet, not a cliff. Nothing regresses
from day one, and each workstream raises its own floor as it lands.

### 0.5 Conventions document

`docs/testing-conventions.md`: naming, one-behaviour-per-test, what belongs in a unit test versus an
integration test, the requirement that every generator test compiles its output, and the rule that a
test asserting a documented behaviour cites the doc.

**Phase 0 exit criteria:** all repos build; all existing tests pass; a deliberately broken generator
(emit `var x = ;`) fails a test; a deliberately `internal`-ised public attribute fails a test; CI
fails when a threshold is dropped by 1%.

---

## 6. Workstreams — parallel after Phase 0

Eleven workstreams across ten sections — `aws-batch` and `aws-web` share a section and can go to one
agent or two. Each owns a disjoint set of directories, so agents do not touch the same files. Sized
roughly evenly by expected effort, ordered by risk.

Each entry gives: **owns** (exclusive directories), **must deliver**, **landmines**. The name in
backticks is the workstream's handle — quote it when assigning the work, and it should appear in
branch names and PR titles.

---

### `core-generator` — request model and binding emit
**Risk: highest. This is where §2.1 came from.**

- **Owns:** `Hardened.Framework/src/SourceGenerators/Hardened.SourceGenerator.Tests/`
- **Targets:** `Hardened.SourceGenerator` — `Requests/`, `Shared/`, `Models/` (28.0% line, 22.9% branch)
- **Must deliver:**
  - `OutputCompiles` coverage for every binding source × parameter shape: path token, query string,
    header, body, service, custom binding attribute — each alone, and in combination
  - The three §2.1 regressions as named tests (named binding attribute, metadata with no parameters,
    `[FromHeader]` on the header collection)
  - Handler shapes: `void`, `Task`, `T`, `Task<T>`, `ValueTask<T>`, async, generic service parameters
  - Zero-parameter handlers, and handlers with metadata but no parameters
  - `BaseRequestModelGenerator` filter-attribute detection
  - Model equality / incremental caching beyond the existing `ModelEqualityCachingTests`
- **Landmines:** the routing-tree tests here are good and already pass — do not rewrite them. The
  overlapping-route-token-name behaviour was fixed recently; assert it rather than reverting it.

### `web-routing` — web generator, routing and the verb matrix

- **Owns:** `Hardened.Framework/src/SourceGenerators/Hardened.Web.SourceGenerator.Tests/`,
  `Hardened.Framework/src/Web/Hardened.Web.Runtime.Tests/`
- **Targets:** `Hardened.Web.SourceGenerator` (34.1%), `Hardened.Web.Runtime` (50.9%)
- **Must deliver:**
  - All five verbs generate, compile and route — `[Get]`, `[Post]`, `[Put]`, `[Delete]`, `[Patch]`
  - Same path under different verbs reaches different handlers
  - `[BasePath]` on class, on assembly, and both together
  - Route-tree conflicts: overlapping tokens, wildcards, trailing slashes, case sensitivity
  - **`NullValueResponseHandler`'s per-verb status matrix** — `GET`/`PUT` → 404,
    `POST`/`DELETE`/default → 200. Currently unasserted, and it is the real status behaviour
  - A decision on §2.3: either wire the four status properties through
    `RequestHandlerNameModel` → `ExecutionRequestHandlerInfo` and test them, or delete them from the
    attributes. **Do not leave them declared and inert.** Flag which you chose
  - `[CacheControl]`, `[RawResponse]`, `[Template]` on handlers
- **Landmines:** `[Delete]`/`[Patch]` were fixed on 2026-08-11 — build on
  `HttpMethodController`/`HttpMethodTests`, do not duplicate them.

### `openapi` — OpenAPI generator

- **Owns:** `Hardened.Framework/src/SourceGenerators/Hardened.OpenApi.SourceGenerator.Tests/`,
  `Hardened.Framework/src/IntegrationTests/OpenApi/`
- **Targets:** `Hardened.OpenApi.SourceGenerator` (41.1%) — 116 tests exist but only 6 integration tests
- **Must deliver:**
  - `OutputCompiles` for every emitter (records, enums, interfaces, handlers, validation filters,
    JSON type info) — currently they assert emitted *strings*, never that they compile
  - Spec parsing: `$ref`, nested/inline schemas, `allOf`/`oneOf`, arrays of refs, nullable, enums,
    every parameter `in` location
  - Malformed specs produce `HOAG002` and no crash; empty file; non-OpenAPI YAML and JSON
  - `HardenedOpenApiNamespace` and `RootNamespace` fallback; `ExcludeGeneratedCodeFromCoverage`
  - Multiple specs in one project without collision
  - Validation filters enforce `minimum`/`maximum`/`required`/`pattern` at runtime — assert through
    the integration SUT, not just the emitted string
  - Grow `Hardened.IntegrationTests.OpenApi.SUT.Tests` past 6 tests: error paths, validation
    rejections, every verb
- **Landmines:** `_OpenApiDiagnostic.g.cs` is emitted on every run — snapshot tests must ignore it or
  they churn.

### `pipeline` — execution pipeline

- **Owns:** `Hardened.Framework/src/Requests/Hardened.Requests.Runtime.Tests/`, and a new
  `Hardened.Requests.Abstract.Tests/`
- **Targets:** `Hardened.Requests.Runtime` (40.8% line, **29.1% branch**), `Hardened.Requests.Abstract`
- **Must deliver:**
  - Filter ordering across the whole `ExecutionFilterOrder` / `FilterOrder` range, including ties
  - `IExecutionChain.Fork` — re-running a handler, cloned request/response isolation, nested forks
  - Short-circuiting: a filter that never calls `Next()`
  - `[Retry]` — exhausted retries, body replay through `IMemoryStreamPool`, retry of a
    partially-written response
  - Serialisation: gzip, Brotli, `BadContentEncodingException`, NDJSON streaming
  - Error handling: `IExceptionToModelConverter`, `IResourceNotFoundHandler`, exception in a filter
    versus in the handler
  - `IGlobalFilterRegistry` both overloads, including the per-handler `null` skip
  - The 29.1% branch coverage is the target — these are conditionals, not lines
- **Landmines:** `Hardened.Requests.Testing` carries a transport conformance suite every
  `IExecutionRequest` is held to. Extend it rather than writing parallel assertions.

### `config-runtime` — shared runtime, configuration and environment

- **Owns:** `Hardened.Framework/src/Shared/Hardened.Shared.Runtime.Tests/`
- **Targets:** `Hardened.Shared.Runtime` (51.5% line, **18.4% branch**)
- **Must deliver:**
  - Configuration generator: interface naming, property naming from fields, defaults from
    initialisers, `[HideConfigurationField]`, non-trivial field types (delegates, dictionaries)
  - `[FromEnvironmentVariable]`: unset, empty, type conversion, conversion failure, caching after
    first resolution
  - `IAppConfig`: `Amend` ordering, environment-scoped `Amend`, the `Func` overload, `ProvideValue`,
    multiple `IConfigurationPackage`s
  - `ConfigurationManager.GetConfiguration<T>` on an unregistered type throws with a useful message
  - `EnvironmentImpl`: `HARDENED_ENVIRONMENT`, `development` default, dictionary-over-process
    precedence, `CustomData`, `Matches`/`MatchesVariable`
  - `ApplicationLogic`: concurrent `IStartupService`s, one returning `false`, one throwing,
    `StartWithWait` timeout
  - Metrics and the pooling types (`ItemPool`, `MemoryStreamPool`, `StringBuilderPool`) under
    concurrency
- **Landmines:** 18.4% branch means the conditionals are untouched. Table-driven tests over
  environment/config permutations will move this fastest.

### `templates` — template engine and generator

- **Owns:** `Hardened.Framework/src/Templates/Hardened.Templates.Runtime.Tests/`, new
  `Hardened.Templates.SourceGenerator.Tests/`
- **Targets:** `Hardened.Templates.Runtime` (50.2%), the template generator (no tests)
- **Must deliver:**
  - `OutputCompiles` for templates against every model shape
  - Template syntax: `{{model}}`, `{{using}}`, properties, format strings, `{{#each}}`, nesting,
    missing property, helper tokens with 0/1/many arguments
  - All four extensions — `html`, `js`, `css`, `md`
  - `SafeString` versus `string` escaping; XSS through an unescaped helper
  - Helper lifecycles: `Singleton`, `Scoped`, `Transient` — assert instance counts
  - `[TemplatePackage]` `Extensions` and `Token` overrides
  - `[Template]` rendering end to end through the web SUT
- **Landmines:** template files are `AdditionalFiles`; the test project needs the same
  `None Remove` + `AdditionalFiles` pairing as a real consumer.

### ~~`console` — console and commands~~ (withdrawn)

`Hardened.Commands` and `Hardened.Console.SourceGenerator` were removed from the repository rather
than tested. The workstream is withdrawn; there is nothing left for it to own.

The parser held 92.3% line coverage at the point it was deleted, and could not execute a single
command end to end — `CommandsLibrary` never pulled in `HardenedCoreModule`, so binding died on an
unresolved `IJsonSerializer` on the first real invocation, including in the repository's own sample.
The item this workstream listed as its priority — *"integration tests for
`Hardened.IntegrationTests.Console.SUT`, it exists, builds every CI run, and has never been executed
by a test"* — is exactly what would have caught it. Worth keeping in view as the argument for the
`Must deliver` lists elsewhere in this plan leading with an end-to-end case rather than closing with
one.

### `test-framework` — the test harness itself

- **Owns:** new `Hardened.Shared.Testing.Tests/`, `Hardened.Framework/src/Web/Hardened.Web.Testing.Tests/`
- **Targets:** `Hardened.Shared.Testing` (49.2% line, 27.2% branch), `Hardened.Web.Testing` (76.6%)
- **Must deliver:**
  - `[HardenedTest]` parameter injection: services, `ITestContext`, `[Mock]`, mixed, unresolvable
  - `[Mock]` registered last and actually winning over the application's registration
  - `[HardenedTestEntryPoint]` at assembly, class and method level; narrower wins
  - `[EnvironmentName]` / `[EnvironmentValue]` precedence across the three levels
  - Registration and startup attribute ordering (`IHardenedOrderedAttribute.Order`)
  - `IRetryEngine`: `TillTrue`, `TillFalse`, `TillValue`, timeout, `Delay`, exception inside the
    predicate
  - `ITestContext.Step` for all four overloads — pass and fail logging, duration, nesting
  - `TestWebResponse.Deserialize` across gzip, Brotli, plain, and a bad encoding
- **Landmines:** this is the harness every other workstream tests *with*. Changes here break
  everyone — additive only, and coordinate any behaviour change.

### `aws-batch` + `aws-web` — AWS Lambda runtimes
**Risk: highest in Amz. 62% of the repo's source has no test project.**

Large enough to split across two agents along the marked line.

- **Owns:** `Hardened.Amz/src/Lambda/**` test projects (new), a restored
  `Hardened.Amz/src/IntegTests/`
- **Targets:** `Function.Lambda.Runtime` (**6.8%**), `Function.Sqs.Runtime`, `Function.DDB.Runtime`,
  `Web.Lambda.Runtime`, `Function.Lambda.Streaming`, and the two Amz generators — all at zero
- **Must deliver — `aws-batch`, function and batch:**
  - `Hardened.Amz.Function.Lambda.Runtime.Tests`: handler resolution by name, unknown name,
    `ILambdaContextAccessor`, `[ThrowException]` versus the default serialise-and-return
  - `Hardened.Amz.Function.Sqs.Runtime.Tests`: **partial batch responses** — all succeed, one fails,
    all fail, `ItemIdentifier` matches the right `MessageId`, `ISqsExceptionHandler` returning
    true/false, `IBatchProcessorExceptionHandler`
  - `Hardened.Amz.Function.DDB.Runtime.Tests`: `[NewImage]`/`[OldImage]` binding, wrong-type
    `InvalidCastException`, `INSERT`/`MODIFY`/`REMOVE`, `StreamsEventResponse` failure identification
  - Restore the deleted `IntegTests/DynamoDbStreamApp` and `SqsTest` harnesses
- **Must deliver — `aws-web`, web and generators:**
  - `Hardened.Amz.Web.Lambda.Runtime.Tests`: **API Gateway payload mapping for both
    `ProxyIntegrationType` values** — method, path, query, headers, base64 bodies, cookies,
    status and header mapping on the way out
  - `Hardened.Amz.Function.Lambda.SourceGenerator.Tests` and
    `Hardened.Amz.Web.Lambda.SourceGenerator.Tests` with `OutputCompiles`
  - `Hardened.Amz.Function.Lambda.Streaming.Tests` — mirror the existing
    `Web.Lambda.Streaming.Tests`, which are good and at 68%
  - `Hardened.Amz.Web.Lambda.Harness.Tests`: an HTTP request converts to an event and back
- **Landmines:** `IntegTests/DynamoDbStreamApp`, `IntegTests/DynamoDbStreamApp.Tests` and `SqsTest`
  exist as empty untracked directories — the harnesses were removed in a "Restore the SQS and DDB
  test harnesses" commit that did not restore them. Recover from git history before rewriting.

### `aws-clients-cdk` — AWS clients, CDK and Lambda test harnesses

- **Owns:** `Hardened.Amz/src/Clients/**` tests, new `Hardened.Amz.Cdk.Tests/`, new tests for the
  four `*.Testing` packages
- **Targets:** `Hardened.Amz.Cdk` (540 lines, zero), `Function.Lambda.Testing`,
  `Shared.Lambda.Testing`, `Sqs.Testing`, `DDB.Testing` (all zero).
  `DynamoDbClient` is at 97.6% — leave it alone
- **Must deliver:**
  - CDK: stack topological ordering by `Produces`/`Consumes`, `Order` tie-breaking, `ShouldDeploy`
    opt-out, `CdkResourceRef<T>` get/set/nullable, missing `ICdkConfigurationProvider` message,
    `StageType.IsProduction`, `KnownRegion`
  - `DynamoDbClientProvider.DefaultClientSettings` — the `ServiceUrl` + `AWS_REGION` interaction that
    silently signed as `us-east-1`. Assert `AuthenticationRegion` directly
  - The `*.Testing` packages are shipped API: `LambdaTestApp.Invoke`/`InvokeRaw`,
    `TestLambdaContext.FromName` defaults, `TestSqsApp` message-id assignment,
    `TestDynamoDbStream.ProcessUpdates`
  - `[LocalDynamoDb]`: image pinning, container reuse per image, `DdbSetup` running per test
- **Landmines:** Testcontainers needs Docker. Mark these tests with a trait so a machine without
  Docker can exclude them explicitly rather than failing mysteriously.

---

## 7. Sequencing

```
Phase 0  ──────────────────────────────────────────────────────────►  (blocking, 1 agent)
             │
             ├─► core-generator      request model + binding emit   ─┐
             ├─► web-routing         web generator + verb matrix     │
             ├─► openapi             spec parsing + emitters         │
             ├─► pipeline            filters, chain, serialisation   ├─  fully parallel,
             ├─► config-runtime      config models + environment     │   disjoint files
             ├─► templates           engine + generator              │
             ├─► console             commands + console generator    │
             ├─► aws-batch           function, SQS, DDB streams      │
             ├─► aws-web             API Gateway + Amz generators    │
             └─► aws-clients-cdk     CDK + the *.Testing packages   ─┘
             │
             └─► test-framework      the harness itself   ─  additive only;
                                                             coordinate behaviour changes
```

**`test-framework` is the one ordering constraint.** It tests the harness the others test *with*, so
a behaviour change there breaks every other workstream mid-flight. Either run it first, or hold it to
additive-only changes.

`core-generator` and `web-routing` both touch generator behaviour but own separate test projects; the
§2.3 decision belongs to `web-routing` alone so it is made once.

`aws-batch` and `aws-web` are one workstream split along the marked line — take both if one agent has
the capacity, since they share the Lambda invocation path.

---

## 8. Conventions for parallel agents

1. **Own your directories, touch nothing else.** Phase 0 registers every project in the `.sln`
   precisely so no agent edits a shared file.
2. **Every generator test compiles its output.** A test that asserts on an emitted string without
   `OutputCompiles` does not count as coverage of a generator.
3. **Name the behaviour, not the method.** `NullReturnOnGetIs404`, not `TestNullHandler`.
4. **A test that encodes a past defect says so**, with the date and what shipped broken — see
   `ParameterBindingTests` and `HttpMethodTests` for the house style.
5. **Assert the observable outcome.** For batch handlers that means `BatchItemFailures` identifiers,
   not counts — the count being right while the identifiers are wrong is the actual failure mode.
6. **Raise your assembly's coverage threshold in `coverlet.runsettings` in the same PR.** The ratchet
   only works if it is turned.
7. **Report contradictions, do not paper over them.** If a documented behaviour does not exist, say
   so rather than writing a test that asserts the bug. §2.3 is the template.
8. **Do not rewrite passing tests.** The routing-tree, streaming and conformance suites are good.

---

## 9. Out of scope

**`Hardened.Canaries`** is excluded. It was removed from the documentation site on 2026-08-11, and its
source still uses the pre-upgrade DependencyModules API (`[Expose]`, `[Singleton]`) that the other two
repositories have moved off. Testing it would mean first deciding whether it is being kept. Its single
package has one test project, and the same plan shape applies if it comes back into scope — say so and
it becomes `canaries`.

**Wiring the route status properties (§2.3)** is a behaviour change, assigned to `web-routing` as a
decision, not scheduled here as work.

**Performance and cold-start benchmarks.** Worth having for an AOT-focused framework, but a different
kind of work with a different definition of done.

---

## 10. Verifying progress

```bash
# Framework
cd Hardened.Framework/src
dotnet test Hardened.Framework.sln -c Release \
  --collect:"XPlat Code Coverage" --settings ../coverlet.runsettings \
  --results-directory ./coverage

# AWS
cd Hardened.Amz
dotnet test Hardened.Amz.sln -c Release \
  --collect:"XPlat Code Coverage" --settings coverlet.runsettings \
  --results-directory ./coverage

# Per-assembly summary
reportgenerator -reports:"coverage/**/*.cobertura.xml" \
  -targetdir:coverage-report -reporttypes:"TextSummary;Html"
```

Baseline for comparison, measured 2026-08-11: framework **11694/32060 lines (36.5%)**, branch
**32.7%**; AWS **666/1126 (59.1%)** across the five assemblies that load, with eleven more absent.

---

## 11. Execution log — 2026-08-11

The workstreams in §6 were run in parallel and stopped partway to bound cost. What landed was
reviewed, completed and committed rather than discarded. Both repositories build clean and pass.

| | Before | After |
|---|---:|---:|
| Framework tests passing | 588 | **1677** |
| AWS tests passing | 120 | **445** |
| Framework build warnings | 0 | 0 |
| AWS build warnings | 10 | **1** |

The one remaining AWS warning is a `CS8625` inside generator output, fixed at source in the
framework (`ApplicationRootImplementation`) and reaching `Hardened.Amz` only when a package
carrying that fix is published.

`coverage-baseline.json` was deliberately **not** re-ratcheted. Eleven agents editing one file
concurrently clobber each other, and a per-workstream run cannot see merged coverage. Baselines are
floors and coverage only rose, so CI stays correct — but the ratchet in §8 of
`testing-conventions.md` has not been turned for this round, and that is the one convention this
run knowingly did not follow.

### 11.1 §2.3 is resolved — the status properties were deleted

`web-routing` deleted them. It also found the plan undercounted: **sixteen** properties across
**four** attributes, not four across two. Only `NullReturnStatus` had an interface slot anything
consumed. Wiring the rest meant designing new response-status behaviour across
`Hardened.Requests.Abstract`, `Hardened.Requests.Runtime` and the serialization and error paths —
not wiring. See the commit for the full reasoning.

### 11.2 Contradictions found, reported not asserted

Each of these is a gap between what the code claims and what it does — the §2 pattern. None has a
test asserting the broken behaviour, per `testing-conventions.md` §6.

**OpenAPI parameter schemas are not validated.** `petstore.yaml` declares `minimum: 1` and
`maximum: 100` on the `limit` query parameter; `?limit=0` and `?limit=101` both return `200`. Body
validation *does* work — a bad `tag` pattern and a missing required `name` each return `400` with a
field-level error — so the gap is specific to parameter schemas, not to validation generally.

**Incremental caching rests on instance reuse, not on the comparer.**
`WebIncrementalGenerator.CombinedComparer` compares both halves by reference.
`EntryPointSelector.Model` declares no `Equals` override, so `x.Item1.Equals(y.Item1)` binds to
`object.Equals` — while `EntryPointSelector.Comparer`, in the same file, compares structurally and
is never used. A rebuilt-but-identical entry point misses the cache and regenerates. Safe, since
the output is identical, but weaker than the caching work implies.

**`--help` is broken for an application with only a root command.**
`CommandLineDefinitionService.GetRootCommand` selects the empty-named command, but `GenerateTree`
still adds every parentless definition — including that root — to the top-level dictionary. The
root becomes its own child, so `PrintHelp` takes the subcommand branch and prints
`Usage: app  <subcommand> [options]` above an empty `Commands:` header instead of the options.
`WriteCommandOptionsHelp` is therefore near-unreachable from `Help`, and throws on `Max()` over an
empty option list if reached. It is covered through the `MissingOption` path, which does reach it.

**`CommandLinePrinter` bypasses its own output seam.** Blank lines, "command not found" and
"missing subcommand" go straight to `Console.WriteLine` rather than `IConsoleOutputService`, so
they cannot be asserted without redirecting global process state.

**Every generated application root carried a `CS8625`.** The emitted `DisposeAsync` assigned
`RootServiceProvider = null` against a non-nullable field, failing any consumer that builds with
`TreatWarningsAsErrors`. Fixed — it now emits `null!`, matching the `?? throw` in the `Provider`
getter beside it.

### 11.3 The recovered AWS harnesses need a composition story

`IntegTests/DynamoDbStreamApp`, `IntegTests/DynamoDbStreamApp.Tests` and `SqsTest` were recovered
from history (commit `75a3f6a` claimed to restore them and did not), brought onto current package
versions and xunit.v3, and migrated off `[Expose]`/`[Singleton]` to `[SingletonService]`. They
compile.

They do **not** run, and are deliberately absent from `Hardened.Amz.sln` until they do. The
generated application constructor resolves `ILambdaInvokeFilterProvider` and nothing registers it:
dropping `[SqsLambda.Module]` also dropped the module composition it performed, and there is no
current replacement. No project in either repository applies another module's registrations, and
`Hardened.Amz.Function.Sqs.Runtime` produces no generated output at all, so `SqsLambda.Module`
cannot be generated.

**This is the one open design question from this round.** How a Hardened runtime module is composed
into a consuming application needs deciding before any end-to-end Lambda harness works — which also
blocks the `aws-batch` integration deliverable in §6.

### 11.4 Workstream status

| Workstream | Status |
|---|---|
| `core-generator` | Partial — caching, model comparison, filter-attribute detection, routing table |
| `web-routing` | **Complete** — see §12; own-area generator 98.3%, `Web.Runtime` 100% line |
| `openapi` | Partial — generator output now compiled; SUT grown; validation gap found |
| `pipeline` | **Complete** — 324 tests, ordering/fork/retry/serialization/errors |
| `config-runtime` | Partial — environment, startup, pooling, generated config wiring |
| `templates` | Partial — generator suite at 168 tests from zero |
| `console` | Partial — parser, definition tree, printer; two defects found |
| `test-framework` | Partial — injection, mocks, entry points, retry engine; additive only |
| `aws-batch` | Partial — runtime suites done; harnesses recovered but not runnable |
| `aws-web` | Partial — harness ported into Amz; payload mapping and streaming covered |
| `aws-clients-cdk` | Partial — CDK covered from zero; warnings cleared |

Remaining per-workstream deliverables from §6 are unchanged and still apply.

---

## 12. `web-routing` completed — 2026-08-12

The workstream's remaining §6 deliverables were finished in a second pass.

| | Before | After |
|---|---:|---:|
| `Hardened.Web.SourceGenerator.Tests` | 58 | **150** |
| `Hardened.Web.Runtime.Tests` | 60 | **128** |
| `AssertNoErrors` call sites | 12 | **21** |
| Generator test cases ending in a compilation assertion | 52 | **144** |
| `Hardened.Web.SourceGenerator` — `SourceGenerator/Web/**` | 97.2% line | **98.3%** |
| `Hardened.Web.SourceGenerator` — assembly headline | 49.1% / 40.8% | **50.5% / 42.0%** |
| `Hardened.Web.Runtime` | 60.6% / 62.5% | **100% / 98.4%** |

The compilation row is the one that matters. Of the twelve `AssertNoErrors` call sites that
existed before, **not one declared a `[HardenedModule]` entry point** — so `RoutingTableGenerator`,
where the route tree, the switch nodes, the wildcard matchers and the dependency-injection method
are emitted, never ran in any of them. They compiled handler invoke classes only. Every case added
here declares one.

The two coverage rows are measured differently and are not comparable to each other. The
`Hardened.Web.Runtime` figures are from that project's own run — the merged number will be at
least as high. The generator headline moves barely at all on purpose: the assembly links
`Hardened.SourceGenerator`'s `Templates/` (812 lines, 0%), `Configuration/` (165, 0%) and
`DependencyInjection/` (61, 0%) as `Compile Include` items and carries the vendored CSharpAuthor
emitter (1672 lines, 60%). None of that is web routing, and writing template tests here to move
the percentage would only hide the `templates` workstream's real number.

### 12.1 The assertion the plan rests on cannot see a generator that crashed

`SourceGeneratorWrapper.Wrap` catches every exception thrown while emitting a file and reports it
as a **`HardenedException` diagnostic at Warning severity**. `GeneratorResult.AssertNoErrors`
filters on `DiagnosticSeverity.Error`, and `GeneratorExceptions` reads the driver's own
`result.Exception`, which the wrapper has already swallowed. So a generator that blew up mid-emit
produces no output, no error, a green `AssertNoErrors`, and a successful build.

This is §2.1 again one level up: the assertion introduced to catch generators that emit
uncompilable C# does not catch generators that emit nothing at all.

`RouteCompilationTests` closes it locally — every case there also asserts that no
`HardenedException` was reported and that the routing table file exists. Doing it properly means
either `AssertNoErrors` failing on `HardenedException`, or the wrapper reporting at Error
severity, and both live outside this workstream's directories.

### 12.2 Contradictions found, reported not asserted

Per `testing-conventions.md` §6, none of these has a test pinning the broken behaviour.

**A single `[Get("")]` deletes the application's entire routing table.** `RouteTreeGenerator.
GenerateTree` throws `All paths must start with '/'`, the wrapper above turns it into a warning,
and the assembly builds with handler classes but no route table and no route. `[Get]` with no
argument at all is fine — `GetPathFromAttribute` defaults to `"/"` only when the argument list is
absent, not when it is present and empty. The same path is reached by `[Get(SomeStaticField)]`,
where a non-constant expression falls back to `argument.Expression.ToString()` and produces a
"path" that is an identifier.

**One character after the last token in a route is dropped, turning the route into a catch-all.**
`[Get("/s/{id}/")]` and `[Get("/s/{id}z")]` both match `/s/7`, `/s/7/`, `/s/7.json` and
`/s/7/other`, binding the whole remainder as `id`. Two or more characters after the token
(`/s/{id}/x`, `/s/{id}.json`) match correctly. `RouteTreeGenerator.ProcessWildCardNodes` consumes
the character following `{TOKEN}` into the node's `Path`, and `RoutingTableGenerator.
WriteWildCardMatchMethod` only ever compares that `Path` from `GenerateWildCardChildMatch`, which
it skips when the node has no children — which is exactly the case when one character was all
that remained.

**A controller in the global namespace produces nothing, silently.**
`WebRequestHandlerModelGenerator.GetInvokeHandlerType` calls `.First()` on the method's namespace
ancestors. With no `namespace` declaration that throws `InvalidOperationException` inside the
syntax-provider transform, and the generator emits no handlers, no routing table and no
diagnostic.

**A controller nested inside another class emits code that does not compile.** A handler on
`TestApp.Outer.Inner` emits an invoke class referencing `TestApp.Inner`: CS0234 and CS0246. The
invoke type is built from the innermost class identifier and the namespace, with no enclosing
types between them.

**`[GetAttribute("/x")]` — the full attribute spelling — routes under the method
`"GETATTRIBUTE"`.** `GetRequestNameModel` does
`attribute.Name.ToString().ToUpperInvariant().Replace("Attribute", "")`, upper-casing before
stripping, so the suffix is `ATTRIBUTE` by the time `Replace` looks for `Attribute` and is never
removed. The emitted route table carries `case "GETATTRIBUTE":`, it compiles cleanly, and no GET
request can reach the handler. Both spellings of an attribute are legal C# and the generator's own
allowlist accepts both.

**`HttpMethodAttribute` is §2.2 again, still shipping.** `public class HttpMethodAttribute { }`
does not derive from `Attribute`, so no project can apply it — the same shape as `[Delete]` and
`[Patch]` before 2026-08-11. It is in the generator's verb allowlist and in the shipped public
surface, and `GetRequestNameModel` throws `NotImplementedException("HttpMethodAttribute not
supported yet.")` for it, so making it applicable without also implementing it would turn an
unusable attribute into a crashing one.

**~~A Brotli static asset is served labelled `gzip`.~~ Fixed.**
`StaticContentHandler` wrote `Headers[Content-Encoding] = KnownEncoding.GZipStringValues`
unconditionally, so a client that offered `br` received Brotli bytes under `Content-Encoding: gzip`
and could not decode them. The response now names the coding the entry holds, and
`ABrotliSiblingIsServedCompressedToAClientThatAskedForIt` asserts it rather than describing it.

Worth recording what kept it invisible. The branch that emitted the wrong label was almost never
reached: the match asked `encoding.Contains(cacheEntry.ContentEncoding)` on a `StringValues`, which
is element equality against the whole header value, and a browser sends `Accept-Encoding: gzip,
deflate, br, zstd` as one value. No browser ever took the serve-the-stored-bytes path at all — every
one of them received the asset inflated per request, which is both the most expensive branch
available and the reason a mislabelled `.br` file never produced a report. `AcceptEncodingHeader`
answers that question now, and `ABrowserAcceptEncodingHeaderGetsTheStoredBytes` pins it against the
header a real client sends.

**`RawResponseAttribute` discards its content type.** `RawResponseAttribute(string contentType =
"text/plain")` has an empty body and no property, so the value exists only in syntax. The web
generator reads it from there and the behaviour is correct; a filter reading the attribute out of
handler metadata would find nothing. `IsFilterAttribute` excludes it from metadata, so nothing
does today.

### 12.3 Static content is a route as of this change

Static content was a fall through: `WebExecutionHandlerService` called `IStaticContentHandler`
directly once routing had failed. Nothing that hangs off a handler reached it — no filter chain, no
conventions, no authorization, no HEAD handling, no 405, no `RequestMapped`. An application that
adopted `[RequireAuthorization]`, whose premise is that an unannotated handler is denied rather than
public, still served everything under its content root anonymously and got no diagnostic saying so.

It is now `StaticContentMountProvider`, registered ahead of every other provider so it is consulted
after them, building its handler through `ExecutionHelper.AsyncStandardFilterEmptyParameters` — the
same helper every generated handler funnels through, and where `CreateFilterArray` applies
conventions and asks `IGlobalFilterRegistry`. This is the move `a82f026` made for `OpenApiUiProvider`
and documented the reasoning for; static content predated it.

The authorization design is that there is none: the mount's metadata carries no `[AllowAnonymous]`,
which is the one thing a convention cannot narrow, so a mount inherits the application's posture.
`IStaticContentConfiguration.Requirement` is there for a mount that wants a policy of its own, which
is what `IExecutionRequestHandlerInfo` documents for a handler registered by hand.

`IStaticContentHandler` and `StaticContentHandler` are deleted rather than deprecated. Left
registered they were the bypass one `GetRequiredService` away, which is the thing this change exists
to remove. `WebExecutionHandlerService`'s constructor loses its parameter with them; both appear in
the approved public API diff.

`StaticContentMountProviderTests` covers what could not previously be asserted — that default-deny
refuses an anonymous request for a file, that a mount requirement reaches the handler info and
refuses, that a HEAD matches the same handler, and that a write to a file is a 405 while a write to a
path only the single-page fall back answers is not.

### 12.4 Static content moved to its own package

`Hardened.Web.StaticContent`. Referencing the package and writing `[HardenedStaticContent]` is the
opt-in; an application that does not carries none of the code and cannot serve a file by accident.
Measured before the split: the feature was 17,408 bytes of `Hardened.Web.Runtime.dll` plus 3,072 in
`Hardened.Shared.Runtime.dll`, none of it trimmable, because `HardenedWebModule` registered the
source and the mount provider unconditionally and a DI registration roots everything it touches.

`FileExtToMimeTypeHelper` deliberately stayed in `Hardened.Shared.Runtime`. Mapping an extension to
a content type is a common problem rather than a static-content one.

Ordering moved into the type system with it. The mount is an `IFallbackRequestHandlerProvider`,
consulted after every ordinary provider, because a directory of files can shadow any path at all and
"registered first, so consulted last" was a property of the registration site — which stops being
controllable the moment the provider ships in its own package and the application decides where its
module goes.

`Hardened.Web.Runtime` keeps `IStaticContentHandler`'s removal from §12.3; the whole `StaticContent`
namespace leaves it here. Both approved public API files were regenerated and reviewed.

### 12.5 The remaining phases

**Not caching, rather than watching.** `IStaticContentConfiguration.CacheContent`, defaulted off in
`development`. A watcher, its change tokens and its invalidation are all machinery for avoiding a
stat and a read of a file the operating system already has in its page cache, which costs tens of
microseconds. Compression follows the flag: paid once into a cache it is recovered on every request
after, and paid per request at `SmallestSize` it is the slowest thing on the path.

It replaces `Debugger.IsAttached`, sampled once in a singleton constructor - so it answered "was a
debugger attached when the container was built", and disabled only the write half of the cache while
every request still paid a lookup that could never hit.

**Ranges and the date validator.** `Accept-Ranges`, 206, 416 with the length attached, `If-Range`,
`Last-Modified` and `If-Modified-Since`. Ranges apply only to a representation served as stored: a
byte offset into a gzip stream is not a byte offset into the resource, and `Content-Range` has no way
to say which one it meant. `If-None-Match` outranks the date outright per RFC 9110 §13.2.1, including
when it does not match.

**A manifest computed at build.** `Hardened.Web.StaticContent.BuildTask` walks the content directory
and writes the C# the application compiles: hashes with SHA-256, compresses once at `Optimal`,
resolves default documents, resolves links, and reports what it will not publish quietly.
`ManifestContentSource` then serves from a table fixed before the process starts - nothing hashed on
the request path, nothing compressed, nothing that grows, and no traversal question at all, because
a path not in the table does not exist.

Diagnostics: HSTATIC001 missing directory, HSTATIC002 a link out of the root, HSTATIC003 a
secret-looking file, HSTATIC004 an empty directory, HSTATIC005 a missing fall back file, HSTATIC006
and HSTATIC007 from the targets file when the manifest does not reach the compilation.

Two defects the tests found in the new code rather than the old, both of the shape this plan exists
to catch:

- The directory alias was chosen by whichever default document the file system walk reached first,
  which on an ordinal walk is `index.htm`. `DefaultDocuments` declared a preference order that
  nothing applied.
- Every diagnostic code was passed to `Log.LogError` positionally, which put it in the *subcategory*
  rather than the code. MSBuild printed the message without an identifier and `<NoWarn>HSTATIC003`
  matched nothing - so the one lever the diagnostic documents did not work.

`GeneratedManifestCompilesTests` compiles the emitted source with Roslyn and loads it back, which is
the assertion §2.1 wanted: a generator whose output does not compile is a defect that surfaces in
somebody else's project, pointing at code they never wrote.

**Not verified here.** The targets file's behaviour under IDE evaluation - Rider and Visual Studio
building their project model out of evaluation rather than a build - is written to the pattern
`Hardened.OpenApi.SourceGenerator.targets` established and its comments record, but nothing in this
repository exercises it. It wants a project that consumes the produced package.

### 12.6 Integration coverage, and the parity it exposed

`Hardened.IntegrationTests.StaticContent.SUT` and `...StaticContent.Manifest.SUT` serve the same
content directory - linked, not copied - through the real pipeline, one from the file system and one
from a build-time manifest. They share one test file, so a claim that the two sources behave alike
is asserted rather than assumed. This is §12.3's missing half: nothing else in the repository
exercised the targets file, the MSBuild task, or the generated manifest reaching a compilation.

Building them found five defects that unit tests could not:

**Every value-typed setting on `[HardenedStaticContent]` was silently ignored.** DependencyModules
unwraps `Nullable<T>` when it generates the module attribute, so `bool?` became `bool` and the guard
it emits is `if (value != null)` - always true, which the generator knows, because it emits
`#pragma warning disable CS0472` above it. `[HardenedStaticContent]` written with no arguments
therefore turned off validators, compression, caching and ranges. The `HardenedOpenApiUi` remarks
describe the guard as applying "only for a nullable one", which is true for a reference type and not
for a value type. Everything but `Path` and `FallBackFile` moved to `ConfigureStaticContent`.

**Both sources auto-registered against `IStaticContentSource`.** `[SingletonService(Try)]` on each
meant two implementations of one interface with `TryAdd`, and the first emitted won - so the
manifest was built, compiled in, and never used. Which source answers is a decision made once in
`ConfigureServices`, not a registration race.

**The manifest served hidden files the file system source refused**, making the build task the less
safe of the two. **It also emitted `app.js.gz` as a route** rather than as `/app.js` stored
compressed, so the sibling workflow served a deflate stream at a name nothing asks for and left
`/app.js` answering nothing. **And it compressed below the threshold the runtime uses**, so a
100-byte file answered `Vary` and refused ranges in one source and not the other.

The remaining gap in the parity is recorded rather than fixed: neither source streams. Both call
`File.ReadAllBytesAsync`, and nothing uses `IHttpResponseBodyFeature.SendFileAsync`, so a large
asset is fully buffered. It needs `StaticContentEntry` to express bytes it does not hold, which the
writer and the range path both read directly.

