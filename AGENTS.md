# Hardened.Framework

Invariants and traps for anyone editing this repository. `README.md` covers what the framework is
and how an application consumes it; this file does not repeat that.

## Layout

The solution is `src/Hardened.Framework.sln`. There is none at the repository root.

| Path | Contents |
|---|---|
| `src/Shared` | Module entry points, configuration, environment, metrics, the test framework |
| `src/Requests` | The execution pipeline and its abstractions |
| `src/Web` | Routing, the Kestrel and ASP.NET Core hosts, static content, the web test client |
| `src/Templates` | The `dotnet new` templates, and RazorBlade view rendering |
| `src/SourceGenerators` | Every generator and build task, and the shared library they build on |
| `src/IntegrationTests` | Working applications driven through the real pipeline |
| `src/PublicApi` | The approved public surface of every shipped assembly |
| `src/Benchmarks` | The figures in the Kestrel host's README |

## Commands

```bash
dotnet build src/Hardened.Framework.sln
dotnet test  src/Hardened.Framework.sln
```

Before opening a pull request, build the way CI does:

```bash
dotnet build src/Hardened.Framework.sln --configuration Release -p:ContinuousIntegrationBuild=true
```

`ContinuousIntegrationBuild` sets `TreatWarningsAsErrors` (`src/Directory.Build.props`). Local
builds deliberately do not, so a build that is green locally can still fail CI on a warning.

**Check the exit code, not the tail of the output.** A restore that resolves an assembly two ways
prints MSB3277 conflict lines by the hundred, which fills a `head -N` window and hides the real
error above it. Capture to a file and test `$?`.

## Two SDKs, and both are load-bearing

`src/global.json` pins the build to a .NET 11 preview, which is the compiler that can read a C# 15
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

**A build task in this repository is built by the same build that consumes it.** The OpenAPI and
Smithy targets run their tasks through `TaskHostFactory` for that reason. If you opt into
`HardenedOpenApiInProcessTask=true` or `HardenedSmithyInProcessTask=true`, the assembly stays
locked and a stale one runs — build with `-nr:false`.

**Generated sources under `obj/**/generated/` are not cleaned by a rename.** A generator that
changes name leaves its old directory behind, and Rider compiles both. Debug and Release have
separate directories, so an IDE reading one while you build the other reports errors `dotnet build`
does not. Delete `obj/` when the IDE and the CLI disagree.

## Smithy needs the CLI

The integration fixture compiles `.smithy` sources with the Smithy CLI, pinned by
`$(HardenedSmithyCliVersion)` in `Hardened.Smithy.SourceGenerator.targets` — **1.73.0** today. The
build enforces the pin rather than trusting it: a mismatch is `HSMT011`, an error under
`ContinuousIntegrationBuild` and a warning otherwise.

`scripts/verify-templates.sh` **skips the smithy combinations silently** when the CLI is absent.
Install it before trusting a green run from that script.

## The approved public surface

`src/PublicApi` checks in the public surface of every shipped assembly and compares it on every run.
A diff there means the shipped contract changed: review it as an API change, then re-approve
deliberately.

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
line is `0.17.0-rc1000`; there was no 0.7.0. Do not describe versions as `1.0.0-*`.

**The pack list in `release.yaml` is hand-maintained and has drifted four times.** A new packable
project has to be added to it *and* to `EXPECTED`, which is a literal on purpose. Adding it to the
solution alone ships a release missing that package.

`0.8.0-rc1000` was a bad release — three unusable packages, superseded by `0.9.0-rc1000`. Never
recommend it.

Dry-run a release before tagging: pack at the real version into a local folder feed and restore a
generated project against it, with `NUGET_PACKAGES` redirected so the global cache is not poisoned.

## Things that will catch you out

**Editing `.sln` through `dotnet sln`.** `dotnet sln remove` followed by `dotnet sln add
--solution-folder` silently drops projects and exits 0. Edit the solution file directly and check
the diff.

**An optional `CancellationToken` on a shared test helper.** Every call site that omits it trips
`xUnit1051`, which is a warning locally and an error under `ContinuousIntegrationBuild` — so the
failure is CI-only and lands at dozens of call sites at once.

**Check `main` is synced before branching.** An unpushed local commit gets absorbed into your pull
request's squash merge.

**`Hardened.SourceGenerator` ships source, not an assembly.** Green CI here says nothing about
whether that source compiles in a consumer. Validate a change to it against Hardened.Amz.

**Placement between `Abstract` and `Runtime`.** The contract stays in `Hardened.Requests.Abstract`;
behaviour moves. A type a function handler needs cannot move to `Hardened.Web.Runtime` — the Lambda
function runtimes do not reference it.

## Where the rest is written down

- `docs/testing-conventions.md` — what to assert, and what not to
- `docs/generator-diagnostics.md` — every diagnostic the generators raise
- `docs/described-authorization.md` — what a contract's `security` becomes
- `docs/validation-usage.md` — constraints, custom validators, the error response
- `docs/response-caching.md` — `[CacheResponse<T>]`, the store package, who a stored answer is for, invalidating by tag
- Full user documentation: <https://ipjohnson.github.io/Hardened.Docs>
