# Hardened

Invariants and traps for anyone editing this repository. `README.md` covers what the framework is
and how an application consumes it; this file does not repeat that.

## Layout

`Hardened.slnx` at the repository root holds every project, and that is what CI builds.
`filters/framework.slnf` is what an editor opens. A cloud gets a filter of its own when it has
projects to filter.

The one exception is `Hardened.Simulators.slnx`: the test projects that run a fixture inside a
cloud's host image against its emulator live there and nowhere else, one folder per cloud. CI
restores, builds and tests it as its own step after the coverage run. It is a second solution
rather than a trait, because `dotnet test --filter` filters nothing on the pinned SDK with xunit.v3;
a trait filter ran every test in both steps.

The repository root holds the two solutions, `AGENTS.md`, `LICENSE`, `README.md`, and the two files
a tool will only read from the directory it is invoked in: `global.json` and `nuget.config`. Every
other build input lives below it. `src/Directory.Build.props` and `src/Directory.Packages.props`
are found by walking up from each project, so they sit beside the projects they apply to; the
`src/Clouds/*/Directory.Build.props` files import them with `GetPathOfFileAbove`. `build/` holds
`coverage.runsettings`, `coverage-baseline.json`, `allocation-baseline.json` and `spectral.yaml`,
each named on the command line by the workflow or gate script that reads it.

| Path | Contents |
|---|---|
| `src/Shared` | Module entry points, configuration, environment, metrics, the test framework |
| `src/Requests` | The execution pipeline and its abstractions |
| `src/Web` | Routing, the Kestrel and ASP.NET Core hosts, static content, the web test client |
| `src/Templates` | The `dotnet new` templates, and RazorBlade view rendering |
| `src/Clients` | The Kiota and Refit test clients |
| `src/SourceGenerators` | Every generator and build task, and the shared library they build on |
| `src/IntegrationTests` | Working applications driven through the real pipeline |
| `src/PublicApi` | The approved public surface of every shipped assembly |
| `src/Benchmarks` | The figures in the Kestrel host's README |
| `docs` | The published site, and the maintainer notes under `design/` |

`Hardened.Docs` was a repository of its own until 2026-09-06 and is now `docs/`. `Hardened.Amz` was
imported and then removed: the AWS *line* is being replaced by new `Hardened.Aws` projects rather
than renamed, so the hosts, their generators and their testing packages earn no place here. It stays
on nuget.org at `0.22.0-rc1000`, restorable and no longer moving, and its history is in this
repository's — `git log` and `git blame` answer for it under `src/Clouds/Aws` at any commit before
it was removed.

`Hardened.Aws.DynamoDbClient` and `Hardened.Aws.DynamoDbClient.Testing` are the exception, restored
from that history rather than rewritten. They are the one part of the Amz line that was not a host:
a client provider and a Testcontainers attribute, binding `Hardened.Shared.Runtime` and
`Hardened.Shared.Testing` and nothing on the host seam. Nothing about them was made wrong by the
rebuild, so replacing them would have been retyping. A further Amz package earns the same treatment
only on the same test — that it touches no host.

## Commands

```bash
dotnet build Hardened.slnx
dotnet test  Hardened.slnx

dotnet build filters/framework.slnf   # for daily work
```

Before opening a pull request, build the way CI does:

```bash
dotnet build Hardened.slnx --configuration Release -p:ContinuousIntegrationBuild=true
```

`ContinuousIntegrationBuild` sets `TreatWarningsAsErrors` (`src/Directory.Build.props`). Local
builds deliberately do not, so a build that is green locally can still fail CI on a warning.

**Check the exit code, not the tail of the output.** A restore that resolves an assembly two ways
prints MSB3277 conflict lines by the hundred, which fills a `head -N` window and hides the real
error above it. Capture to a file and test `$?`.

## Two SDKs, and both are load-bearing

`global.json` pins the build to a .NET 11 preview, which is the compiler that can read a C# 15
`union`. Every project targets `net8.0` and every test assembly is framework-dependent on
`Microsoft.NETCore.App` 8.0.0 with no `rollForward`, and the default policy does not cross a major
version — so a machine with only the .NET 11 SDK compiles everything and then starts no test host
at all. Install both.

The preview is named exactly rather than as `11.0.x`. Keep it that way — a floating preview moves
the SDK underneath a session.

## Generators

**Emitted C# is written with CSharpAuthor.** `StringBuilder` is for ordinary string work, not for
building C#. New emitters use CSharpAuthor; the remaining `StringBuilder` emitters are legacy to
convert.

**CSharpAuthor and ValidationModules.Impl are compiled from source, not referenced.**
`src/SourceGenerators/CSharpAuthor.props` and `ValidationModulesImpl.props` do this, because an
analyzer is loaded by the compiler with no probing path of its own and a sibling DLL is a
`FileNotFoundException` at initialization. Both switch to a sibling checkout when one exists —
`~/CSharpAuthor`, `~/ValidationModules` — which is why a coverage baseline written locally is not
reproducible in CI.

**`CS8785` is an error here, locally as well as in CI.** A generator that throws mid-run has emitted
some of its output and none of the rest, and Roslyn reports that as a warning.

**Verify an analyzer actually reaches the compiler.** `project.assets.json` and
`-getItem:Analyzer` both report a package that never reaches `csc`. Grep the real command line:

```bash
dotnet build -v:d 2>&1 | grep -o '/analyzer:[^ ]*' | sort -u
```

**A build task in this repository is built by the same build that consumes it.** The OpenAPI,
Smithy and document-export targets run their tasks through `TaskHostFactory` for that reason. If
you opt into `HardenedOpenApiInProcessTask=true`, `HardenedSmithyInProcessTask=true` or
`HardenedOpenApiDocumentInProcessTask=true`, the assembly stays locked and a stale one runs — build
with `-nr:false`.

**The three integration applications export their served document to a tracked file.**
`<HardenedOpenApiOutput>` on each SUT writes `openapi/<EntryPoint>.json` after every compile, and
each suite's `ExportedDocumentTests` compares it with `/openapi.json`. A route change therefore
shows up as a diff in that file; commit it with the change. The export task reads the served
literal out of the compiled assembly under `{EntryPoint}.OpenApiDocument.GZip`, a name the
generators and `Hardened.OpenApiDocument.BuildTask` both depend on.

**Generated sources under `obj/**/generated/` are not cleaned by a rename.** A generator that
changes name leaves its old directory behind, and Rider compiles both. Debug and Release have
separate directories, so an IDE reading one while you build the other reports errors `dotnet build`
does not. Delete `obj/` when the IDE and the CLI disagree.

## One executor

`IRequestExecutor` in `Hardened.Requests.Abstract`, `RequestExecutor` in
`Hardened.Requests.Runtime`. Everything a host does around the middleware chain — the begin line,
the try/catch, the total duration, the end line, disposing the metric logger — is there and nowhere
else. **A new host does not write its own.**

It was written five times before 2026-09-06, and the cost is on the record: the same defect fixed
three times independently, in three files, by three commits that named none of the others. A request
that threw lost its duration, its end line and its metrics, because the close-out ran as
straight-line statements after the chain rather than in a `finally`.

Three steps rather than one call, because `IHttpApplication<TContext>` hands Kestrel the lifecycle in
pieces: `CreateContext` has to return before `ProcessRequestAsync` is called. `Run` is the same three
in order, for a host that owns the whole invocation.

**The host still owns the scope.** `End` does not dispose one, because the hosts disagree about when
it ends — Kestrel holds it across three callbacks, the Lambda drivers have it in an `await using`,
and ASP.NET Core never made one.

**A throw is `HostFailurePolicy`.** `Answer500` where the server's own handler would log against the
server and abort the connection; `Rethrow` where the runtime marking an invocation failed is the
existing contract and a 500 would hide it from retries and the dead letter queue. A host needing a
third answer writes its own catch and still calls `Begin` and `End`: `PipelineRequest` in
`Hardened.Web.Testing` is the one that does, because it has no connection to tear down and the
exception reaching the caller is how a stream that failed after its first event says so.

`Hardened.Benchmarks` is deliberately not on it. It runs the chain and nothing else, which is what it
is measuring; putting the telemetry back would change the figures in the Kestrel host's README.

## Smithy needs the CLI

The integration fixture compiles `.smithy` sources with the Smithy CLI, pinned by
`$(HardenedSmithyCliVersion)` in `Hardened.Smithy.SourceGenerator.targets` — **1.73.0** today. The
build enforces the pin rather than trusting it: a mismatch is `HSMT011`, an error under
`ContinuousIntegrationBuild` and a warning otherwise.

`scripts/verify-templates.sh` skips the smithy combinations when the CLI is absent or on the wrong
version, and prints why:

```
note: skipping the smithy contract - it needs the Smithy CLI at 1.73.0, found 1.56.0
```

It used to skip them silently, which is what the warning here used to say. Read the note rather than
the exit code — the run is still green, and the smithy rows still did not run.

Passing explicit combinations to that script skips the whole block, note included, because the
smithy rows are only ever added to the default list.

## The approved public surface

`src/PublicApi` checks in the public surface of every shipped assembly and compares it on every run.
A diff there means the shipped contract changed: review it as an API change, then re-approve
deliberately.

Twenty assemblies. A source generator package is not among them: it sets
`IncludeBuildOutput=false` and packs an analyzer into `analyzers/dotnet/cs`, so no consumer binds
against it and there is no `lib` assembly to have a surface.

A new package means a `ProjectReference` in the csproj **and** a name in `Shipped`, and
`EveryShippedAssemblyBesideThisOneIsCovered` holds the second to the first. It reads the assemblies
in the build output rather than `GetReferencedAssemblies`, which asserted nothing: the compiler
records a reference only where a type is used, this test uses none, so the reference set was empty
and the check passed however many packages were missing.

```bash
APPROVE_PUBLIC_API=1 dotnet test src/PublicApi/Hardened.PublicApi.Tests
```

Never set that in CI.

## The coverage gate

`scripts/coverage-gate.py` holds each assembly at the coverage it already had. Raising a floor is a
deliberate commit.

**Never write a baseline from a local run.** The generator assemblies compile their dependencies
from source, and a sibling checkout changes what is in them. Take the summary from CI:

```bash
gh run download <run-id> -n coverage-report -D /tmp/cicov
python3 scripts/coverage-gate.py --summary /tmp/cicov/Summary.json --update
```

**A baseline entry no run reported is fatal; an assembly the run reported and the baseline does not
is only printed.** So moving code between assemblies means renaming the baseline entry in the same
commit, and an assembly that never reaches the report is never gated —
`Hardened.Smithy.BuildTask` is in that state under `ContinuousIntegrationBuild`, which is
unmeasured rather than untested.

## Releasing

A `v*` tag drives `release.yaml`, and the tag is the source of truth for the version. The current
line is `0.22.0-rc1000`; there was no 0.7.0. Do not describe versions as `1.0.0-*`.

**The pack list in `release.yaml` is hand-maintained and has drifted four times.** A new packable
project has to be added to it *and* to `EXPECTED`, which is a literal on purpose. Adding it to the
solution alone ships a release missing that package.

`0.8.0-rc1000` was a bad release — three unusable packages, superseded by `0.9.0-rc1000`. Never
recommend it.

**The Kiota pins follow Kiota's line, not this repository's, and there are two pairs of them.** The
`hardened-web` template pins the tool in `templates/hardened-web/.config/dotnet-tools.json` and the
bundle as `KiotaBundleVersion` in its `Directory.Packages.props`; the repository pins the same pair
for the client it generates over the Web integration application, in `.config/dotnet-tools.json` at
the root and as `KiotaBundleVersion` in `src/Directory.Build.props` — there, because
`src/Directory.Packages.props` pins the package to that property and is imported before any
project body. All four are bumped together,
by a deliberate commit, to one Kiota release; `kiota info --language CSharp --json` says which
bundle a tool expects. `scripts/verify-templates.sh` checks both pairs before it scaffolds anything
and is the gate, as it is for everything else in the template; the two client projects check their
own pair at build (HTPL003).

`Hardened.Kiota.Testing` references the Kiota runtime - `Microsoft.Kiota.Abstractions` and
`Microsoft.Kiota.Http.HttpClientLibrary` - at the bundle's version, not the bundle: a generated
client registers the serializers itself. A consumer on a newer 2.x bundle unifies upward; a bump
of the pins to a new major has to move that reference with them.

**The Refit pair moves by hand.** `--client refit` pins the Refitter tool in the template's
`.config/dotnet-tools.refit.json` and `Refit` in its `Directory.Packages.props`, and
`Hardened.Refit.Testing` references `Refit` at that version too. Refitter does not report the Refit
version it writes for, so there is no HTPL003 for this pair; bump the three together and let the
refit rows of `scripts/verify-templates.sh` say whether the generated interface still compiles.

**`Hardened.IntegrationTests.WebApp.SUT.Client` is the template's client project with the names
changed, on purpose.** It generates a Kiota client from the tracked `openapi/Application.json` into
`obj/` on every build, and `GeneratedClientTests` in the SUT's test project drive it through the
pipeline. Nothing generated is committed. When the template's `src/Hardened1.Client` project
changes, change this one the same way.

Dry-run a release before tagging: pack at the real version into a local folder feed and restore a
generated project against it, with `NUGET_PACKAGES` redirected so the global cache is not poisoned.

## Things that will catch you out

**Editing the solution through `dotnet sln`.** `dotnet sln remove` followed by `dotnet sln add
--solution-folder` silently drops projects and exits 0, and `dotnet sln add` given many projects at
once flattens them into one folder and then refuses on the first name collision. `Hardened.slnx` is
short and readable; edit it directly and check the diff. A project added there has to be added to
its filter too, and to the pack list in `release.yaml` if it ships.

**An optional `CancellationToken` on a shared test helper.** Every call site that omits it trips
`xUnit1051`, which is a warning locally and an error under `ContinuousIntegrationBuild` — so the
failure is CI-only and lands at dozens of call sites at once.

**Two `IHandlerDispatch` registrations in one container.** `[HardenedWebModule]` registers the
routing table as one and the function generator registers `FunctionDispatchFilter` as another, and
the hosts that pick exactly one refuse the pair - which is right on Lambda, where the two are
separate functions, and wrong on Cloud Run, where one service serves both. A module cannot
`RemoveAll` its way out: DependencyModules applies dependencies before dependents and the root
application last, so a module's `ConfigureServices` runs before the generator's registration lands.
`DependencyRegistry<T>.AddDecorator` runs after every module's services and is the hook;
`CloudRunDispatch` in `src/Clouds/Gcp` composes the two through it and registers the result as
`IWebExecutionHandlerService` as well, because `KestrelServerRunner` resolves that directly.

**Check `main` is synced before branching.** An unpushed local commit gets absorbed into your pull
request's squash merge.

**`Hardened.SourceGenerator` ships source, not an assembly.** Its own build says nothing about
whether that source compiles in a consumer, and there is no in-repository consumer of it today —
Hardened.Amz was one until its source was removed. The `Hardened.Aws` generators, if the design ends
up wanting any, compile it in from `src/SourceGenerators/Hardened.SourceGenerator` rather than
restoring the package, which is what makes a break in it fail the same build.

**Every package version is in `src/Directory.Packages.props`.** A `Version` on a `PackageReference` is
`NU1008`. A project that genuinely needs a different version says so with `VersionOverride` and a
comment giving the reason; four do. Adding a package means adding a `PackageVersion` there first.

**Placement between `Abstract` and `Runtime`.** The contract stays in `Hardened.Requests.Abstract`;
behaviour moves. A type a function handler needs cannot move to `Hardened.Web.Runtime` — the Lambda
function runtimes do not reference it.

## Where the rest is written down

- `docs/design/testing-conventions.md` — what to assert, and what not to
- `docs/design/generator-diagnostics.md` — every diagnostic the generators raise
- `docs/design/described-authorization.md` — what a contract's `security` becomes
- `docs/design/validation-usage.md` — constraints, custom validators, the error response
- `docs/design/response-caching.md` — `[CacheResponse<T>]`, the store package, who a stored answer is for, invalidating by tag, revalidating with a 304
- `docs/design/request-timeouts.md` — `[Timeout]`, the four rungs it resolves through, `x-hardened-timeout` and the Smithy `@timeout` trait, tighten-only conventions, why the token is put back
- `docs/design/client-testing.md` — `Returns<T>()` in `Hardened.Web.Testing`, the route and reader seam it reads through, `[assembly: KiotaTesting]` and `[assembly: RefitTesting]`, why there is a package per generator
- `docs/design/filter-rungs.md` — a filter declared on a `[HardenedModule]` class, collected once and read by the document as well as the pipeline; the entry-point rung is built, the assembly rung is not
- `docs/` — the published site. `npm run build` there fails on a dead internal link
- Full user documentation: <https://ipjohnson.github.io/Hardened.Framework>
