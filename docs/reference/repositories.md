# Repositories

Hardened is three repositories. They version and ship independently, so an application only carries
the parts it uses.

## Hardened.Framework

**[github.com/ipjohnson/Hardened.Framework](https://github.com/ipjohnson/Hardened.Framework)**

The core. Modules and dependency injection, configuration, the execution pipeline, web routing and
parameter binding, the template engine, console commands, the test framework, and the source
generators behind all of it.

| Path | Contents |
|---|---|
| [`src/Shared`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/Shared) | Module entry points, configuration, environment, metrics, and the test framework |
| [`src/Requests`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/Requests) | The execution pipeline and its abstractions |
| [`src/Web`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/Web) | Routing, ASP.NET Core integration, the web test client |
| [`src/Templates`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/Templates) | The template engine and its helpers |
| [`src/Commands`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/Commands) | Console command parsing and dispatch |
| [`src/SourceGenerators`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/SourceGenerators) | Every generator, including the shared library they build on |
| [`src/IntegrationTests`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/IntegrationTests) | Working applications driven through the real pipeline. The worked examples in the codebase |

Documented in the [Guide](/guide/getting-started).

## Hardened.Amz

**[github.com/ipjohnson/Hardened.Amz](https://github.com/ipjohnson/Hardened.Amz)**

The AWS integrations: Lambda runtimes for functions, API Gateway, DynamoDB Streams and SQS; DynamoDB
and SQS clients; CDK constructs; and the test harnesses for all of them.

| Path | Contents |
|---|---|
| [`src/Lambda/Function`](https://github.com/ipjohnson/Hardened.Amz/tree/main/src/Lambda/Function) | Plain function invocation, batch filter base, streaming |
| [`src/Lambda/Web`](https://github.com/ipjohnson/Hardened.Amz/tree/main/src/Lambda/Web) | API Gateway, response streaming, the local harness |
| [`src/Lambda/DynamoDbStream`](https://github.com/ipjohnson/Hardened.Amz/tree/main/src/Lambda/DynamoDbStream) | Stream records, `[NewImage]`, `[OldImage]` |
| [`src/Lambda/Sqs`](https://github.com/ipjohnson/Hardened.Amz/tree/main/src/Lambda/Sqs) | SQS batches and partial batch responses |
| [`src/Lambda/Shared`](https://github.com/ipjohnson/Hardened.Amz/tree/main/src/Lambda/Shared) | Structured logging, embedded metrics, stage and region types |
| [`src/Clients/DynamoDb`](https://github.com/ipjohnson/Hardened.Amz/tree/main/src/Clients/DynamoDb) | `IDynamoDbClientProvider` and DynamoDB Local |
| [`src/Hardened.Amz.Cdk`](https://github.com/ipjohnson/Hardened.Amz/tree/main/src/Hardened.Amz.Cdk) | CDK constructs and the deploy command |
| [`src/SourceGenerators`](https://github.com/ipjohnson/Hardened.Amz/tree/main/src/SourceGenerators) | The Lambda function and web generators |

Documented under [AWS](/aws/).

## Hardened.Docs

**[github.com/ipjohnson/Hardened.Docs](https://github.com/ipjohnson/Hardened.Docs)**

This site. Built with [VitePress](https://vitepress.dev) from Markdown in `website/`, and published
to GitHub Pages on every push to `main` that touches it.

```
cd website
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
