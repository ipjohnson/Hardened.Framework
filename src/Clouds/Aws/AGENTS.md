# Hardened.Amz

Invariants and traps for anyone editing this repository. `README.md` covers what the packages are
and how an application consumes them, and `docs/application-types.md` covers the module attribute
each transport needs; this file does not repeat either.

## Layout

The solution is `Hardened.Amz.sln` at the repository root.

| Path | Contents |
|---|---|
| `src/Lambda/Function` | Direct invocation, the batch filter base, response streaming |
| `src/Lambda/Web` | API Gateway, response streaming, the local harness |
| `src/Lambda/DynamoDbStream` | Stream records, `[NewImage]`, `[OldImage]` |
| `src/Lambda/Sqs` | SQS batches and partial batch responses |
| `src/Lambda/Shared` | Structured logging, embedded metrics, stage and region types |
| `src/Clients/DynamoDb` | `IDynamoDbClientProvider` and DynamoDB Local |
| `src/Hardened.Amz.Cdk` | CDK constructs and the deploy command |
| `src/SourceGenerators` | The Lambda function and web generators |
| `src/IntegTests`, `src/LambdaWebTest`, `src/SqsTest` | Sample applications driven through the real pipeline |

## Commands

```bash
dotnet build Hardened.Amz.sln
dotnet test  Hardened.Amz.sln
```

Before opening a pull request, build the way CI does:

```bash
dotnet build Hardened.Amz.sln -p:UseLocalHardenedFramework=false -p:ContinuousIntegrationBuild=true
```

`ContinuousIntegrationBuild` turns warnings into errors, so a build that is green locally can still
fail CI on a warning.

The DynamoDB client tests need a running Docker daemon. They **fail rather than skip** without one.

## The sibling checkout

`src/Directory.Build.targets` prefers a sibling `../Hardened.Framework` checkout over the pinned
packages when one exists. That is what lets the two repositories be edited together, and it is why
a build can fail with errors in *the other repository's* files. Force it either way:

```bash
dotnet build Hardened.Amz.sln -p:UseLocalHardenedFramework=false   # pinned packages, as CI builds
dotnet build Hardened.Amz.sln -p:UseLocalHardenedFramework=true    # the sibling checkout
dotnet build Hardened.Amz.sln -p:HardenedFrameworkRoot=/path       # a checkout somewhere else
```

Only the mapped packages a project already references are swapped. Adding a new Framework
dependency means adding it to `HardenedLocalLibrary` or `HardenedLocalAnalyzer` in that file, or the
local build silently keeps using the published package for that one.

**This repository is where source-shipped Framework packages get validated.**
`Hardened.SourceGenerator` ships source rather than an assembly, so green Framework CI says nothing
about whether it compiles elsewhere. A Framework change to a generator should be built here before
it is released.

## Versions

Amz releases on the **same version line as the Framework**, from a `v*` tag. The current line is
`0.15.0-rc1000`. Bumping means editing `src/Directory.Packages.props` and waiting for the Framework
packages to reach nuget.org first — `scripts/check-framework-dependencies.py` fails the release
otherwise.

That check exists because `dotnet pack` writes whatever version was *resolved* into the nuspec and
does not care which feed it came from. A preview restored from GitHub Packages becomes a dependency
nobody restoring from nuget.org can satisfy, and a version on nuget.org can be unlisted but never
removed.

**The pack list in `release.yaml` is hand-maintained**, and `EXPECTED` beside it is a literal on
purpose. A new packable project has to be added to both, or the release ships without it.

## The coverage gate

`scripts/coverage-gate.py` holds each assembly at the coverage it already had. Raising a floor is
`--update`, reviewed and committed; never run it in CI.

Two things about this repository's baseline in particular.

**The sample applications are gated.** `DynamoDbStreamApp` and `SqsTest` are in
`coverage-baseline.json`. A Framework bump regenerates their handlers and routing, which grows the
denominator and drops the percentage without anyone changing a test — re-baseline those two after a
bump.

**Never add a dependency assembly to the baseline.** The report has no assembly filter, so
everything loaded reaches it, including `Hardened.*` assemblies from the Framework. A floor on one
of those gates another repository's code from here and moves on edits nobody made in this one.

## Things that will catch you out

**A missing `[assembly: LambdaFunctionTesting]` fails silently.** `MiddlewareService` holds no
filters, the execution chain is empty, the handler never runs, and the invocation returns an empty
stream. A test asserting only "no exception" passes against an application that did nothing.

**Assert partial batch failures by identifier, not by count.** A right count against the wrong
identifiers redelivers every message and deletes the poison one.

**`ProxyIntegrationType.ApiGateway` is not implemented.** Payload format 2.0 is the only one, and
selecting REST API format 1.0 is a build error, `HRDAWS001`.

**No SQS client package exists.** The SQS runtime consumes a queue; writing to one means a direct
`AWSSDK.SQS` dependency.

**Check `main` is synced before branching.** An unpushed local commit gets absorbed into your pull
request's squash merge.

## Where the rest is written down

- `docs/application-types.md` — the module attribute, project shape and test setup per transport
- `docs/testing-conventions.md` — what to assert, and what not to
- Full user documentation: <https://ipjohnson.github.io/Hardened.Docs/aws/>
