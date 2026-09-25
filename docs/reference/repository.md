# Repository

Hardened is one repository,
[github.com/ipjohnson/Hardened.Framework](https://github.com/ipjohnson/Hardened.Framework). It holds
the framework, the cloud packages, the templates and this site.

```bash
dotnet build Hardened.slnx
dotnet test Hardened.slnx
```

`Hardened.slnx` holds every project except the simulator tests: 205 projects.

## Top-level folders and files

| Path | Holds |
|---|---|
| `src/` | Every project: the packages, their tests, the integration tests and the benchmarks |
| `docs/` | This site, and the maintainer notes in `docs/design/` |
| `build/` | The coverage run settings, the coverage and allocation baselines, and the Spectral ruleset that CI lints the OpenAPI document with |
| `filters/` | Solution filters over `Hardened.slnx` |
| `scripts/` | The CI gates and the maintenance scripts |
| `assets/` | The logo images |
| `.github/workflows/` | The CI, template, documentation, benchmark, allocation and release workflows |
| `.githooks/` | A pre-commit hook that refuses unformatted C# |
| `.config/dotnet-tools.json` | The local tools: CSharpier 1.3.0 and Kiota 1.34.1 |
| `Hardened.slnx` | Every project except the simulator tests |
| `Hardened.Simulators.slnx` | The simulator tests |
| `AGENTS.md` | The rules and traps for editing the repository |
| `global.json` | The .NET 11 preview SDK the build uses |
| `nuget.config` | nuget.org as the only package source |
| `README.md` | The repository's readme. Every package carries it as its nuget.org readme |
| `LICENSE` | The MIT License |

`src/Directory.Build.props` holds the build settings for every project.
`src/Directory.Packages.props` holds every package version.

The site's build leaves out `docs/design/`.

## Source folders

| Folder | Holds |
|---|---|
| `src/Shared` | `Hardened.Shared.Runtime`, and the test package `Hardened.Shared.Testing` |
| `src/Requests` | The request pipeline, the response cache store and the serializers |
| `src/Web` | Routing, the Kestrel and ASP.NET Core hosts, static content and the web test packages |
| `src/Functions` | The trigger attributes, the function test package and `Hardened.CloudEvents` |
| `src/Clouds` | `Aws`, `Gcp` and `Azure`: each cloud's host, adapters, test package and integration tests, and the DynamoDB client |
| `src/Templates` | The `dotnet new` templates, and RazorBlade views |
| `src/Clients` | The Kiota and Refit test packages |
| `src/SourceGenerators` | Every generator and build task, and the source they share |
| `src/IntegrationTests` | Applications driven through the real pipeline, and their tests |
| `src/PublicApi` | A test that compares the public API of the shipped assemblies with approved copies |
| `src/Benchmarks` | Benchmarks of the pipeline, with a comparison against ASP.NET Core |

[Packages](/reference/packages) lists every package that the repository publishes.

## Solutions and filters

| File | Holds |
|---|---|
| `Hardened.slnx` | Every project except the simulator tests, 205 projects. CI builds and tests it |
| `Hardened.Simulators.slnx` | The simulator tests, 11 projects. Each runs an application inside its cloud's host image against the cloud's emulator, through Testcontainers |
| `filters/framework.slnf` | The framework: 97 projects, none from `src/Clouds` or `src/Functions` |
| `filters/gcp.slnf` | The Cloud Run packages, their tests and integration tests, and `Hardened.CloudEvents`: 29 projects |
| `filters/azure.slnf` | The Azure Functions packages, their tests and integration tests, and the projects they build on: 51 projects |

A filter opens a subset of `Hardened.slnx` in an editor. AWS has no filter. `dotnet build` also
takes a filter:

```bash
dotnet build filters/framework.slnf
```

## Scripts

| Script | Does |
|---|---|
| `scripts/coverage-gate.py` | Fails when an assembly's line coverage falls below its entry in `build/coverage-baseline.json`. CI runs it after the tests |
| `scripts/allocation-gate.py` | Fails when a benchmark allocates more bytes per operation than its entry in `build/allocation-baseline.json`. The allocation workflow runs it |
| `scripts/verify-templates.sh` | Builds and packs the framework and the templates, installs the packed templates, generates a project for each supported combination of options, and builds, tests and calls each one. The templates workflow runs it |
| `scripts/compare-matcher-runs.py` | Compares two BenchmarkDotNet result tables and prints the change |
| `scripts/generate-route-scale-sut.py` | Writes the route-scale benchmark applications |
| `scripts/orchestration/guard-paths.sh` | A hook that blocks a cloud-line agent from editing files outside the paths it owns |

`python3 scripts/coverage-gate.py --summary <Summary.json> --update` rewrites the coverage baseline.
`AGENTS.md` says to take the summary from a CI run, not from a local run.

`scripts/verify-templates.sh` skips the Smithy combinations when the Smithy CLI is missing or at
another version. It prints a note when it skips them.

## AGENTS.md

`AGENTS.md` holds the rules and traps for editing the repository. Its sections cover the layout, the
commands, formatting with CSharpier, the two SDKs, the generators, the one request executor, the
Smithy CLI, the approved public API, the coverage gate, releasing, and the traps that catch
contributors out. It ends with a list of the maintainer notes in `docs/design/`.

## Building and testing

`global.json` pins the .NET 11 SDK `11.0.100-preview.7.26381.103`, which compiles every project. The
tests target `net8.0`, so they also need the .NET 8 runtime.

The build also needs the Smithy CLI 1.73.0 on `PATH`, for the Smithy integration test. Without the
CLI, the build fails with `HSMT010`. With another version, the build warns with `HSMT011`. A build
with `ContinuousIntegrationBuild=true` fails instead.

`ContinuousIntegrationBuild=true` turns every warning into an error. CI builds with it, in
`Release`:

```bash
dotnet build Hardened.slnx --configuration Release -p:ContinuousIntegrationBuild=true
```

`dotnet test Hardened.slnx` runs every test except the simulator tests.
`dotnet test Hardened.Simulators.slnx` runs the simulator tests. They need Docker.

```bash
dotnet build Hardened.Simulators.slnx
dotnet test Hardened.Simulators.slnx
```

## Documentation site

VitePress builds the site from `docs/`.

```bash
cd docs
npm ci
npm run dev
npm run build
```

`npm run dev` serves the site locally. `npm run build` builds the site. A dead internal link fails
the build. CI runs `npm ci` and `npm run build` on Node.js 22.

## Next

- [Packages](/reference/packages): every package that the repository publishes
- [Diagnostics](/reference/diagnostics): the build diagnostics, such as `HSMT010`
- [Project templates](/guide/project-templates): the templates that `scripts/verify-templates.sh`
  checks
