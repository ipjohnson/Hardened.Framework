# Repository

Hardened is one repository. The framework, the cloud packages and this site release together on one
version, so a change and the page describing it land in the same commit.

**[github.com/ipjohnson/Hardened.Framework](https://github.com/ipjohnson/Hardened.Framework)**

| Path | Contents |
|---|---|
| [`src/Shared`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/Shared) | Module entry points, configuration, environment, metrics, the test framework |
| [`src/Requests`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/Requests) | The execution pipeline and its abstractions |
| [`src/Web`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/Web) | Routing, the Kestrel and ASP.NET Core hosts, static content, the web test client |
| [`src/Functions`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/Functions) | The [trigger](/guide/triggers) attributes and the test façades, naming no cloud |
| [`src/Clouds`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/Clouds) | The [AWS Lambda](/aws/) host and one adapter per source |
| [`src/Templates`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/Templates) | The `dotnet new` templates, and RazorBlade view rendering |
| [`src/Clients`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/Clients) | Kiota and Refit test clients |
| [`src/SourceGenerators`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/SourceGenerators) | Every generator and build task, and the shared library they build on |
| [`src/IntegrationTests`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/IntegrationTests) | Working applications driven through the real pipeline. The worked examples in the codebase |
| [`src/PublicApi`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/PublicApi) | The approved public surface of every shipped assembly |
| [`src/Benchmarks`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/Benchmarks) | The pipeline measured against ASP.NET Core, on the same machine |
| [`docs`](https://github.com/ipjohnson/Hardened.Framework/tree/main/docs) | This site, and the maintainer notes under `design/` |

The framework is documented in the [Guide](/guide/getting-started), the AWS packages under
[AWS](/aws/).

## What used to be separate

`Hardened.Docs` held this site and is now `docs/`, so a page and the change that made it wrong land
in the same commit.

`Hardened.Amz` held the AWS packages. Its history is here and almost none of its source is: the
line was replaced by the `Hardened.Aws.Lambda` packages in `src/Clouds/Aws` rather than renamed, so
there was nothing to carry forward. It stays on nuget.org at `0.22.0-rc1000`, restorable and no
longer moving. The [AWS pages](/aws/) describe the packages that replaced it.

The [DynamoDB client](/aws/dynamodb) and its testing package came across, as
`Hardened.Aws.DynamoDbClient` and `Hardened.Aws.DynamoDbClient.Testing`. They were the only part of
that line that was not a host, so the rebuild left them correct as they stood.

## What is deliberately outside

[LambdaWidgets](https://github.com/ipjohnson/LambdaWidgets) consumes Hardened as packages from
nuget.org, never as a project reference to a checkout beside it. That is the point of it: a local
project reference hides the packaging defects an external consumer is the only thing positioned to
find. Any repository whose value is being an external consumer stays external.

## Building it

```bash
dotnet build Hardened.slnx
dotnet test  Hardened.slnx
```

`filters/framework.slnf` cuts it down for daily work. The repository stays whole; what an editor
loads does not have to be.

The site is [VitePress](https://vitepress.dev), built from `docs/`:

```bash
cd docs
npm ci
npm run dev      # local server with hot reload
npm run build    # what CI runs; fails on a dead internal link
```

Every page has an "Edit this page on GitHub" link at the bottom.

## Related

**[DependencyModules](https://ipjohnson.github.io/DependencyModules/)**,
[github.com/ipjohnson/DependencyModules](https://github.com/ipjohnson/DependencyModules)

Compile-time dependency injection for .NET, and the foundation Hardened's module system is built on.
`[SingletonService]`, `[ScopedService]`, `[TransientService]`, conventions, decorators and
interception all come from there and all work in a Hardened application unchanged.
