# Testing conventions

These apply to `Hardened.Framework` and `Hardened.Amz`. They exist so that eleven workstreams
running in parallel produce one coherent suite rather than eleven dialects.

Written 2026-08-11 as Phase 0 of `TESTING-PLAN.md`.

---

## 1. The rule that matters most

**Every generator test compiles its output.**

```csharp
GeneratorTestHarness
    .Run(source, new WebLibrarySourceGenerator(), Anchors)
    .AssertNoErrors();          // ← compiles the input together with the generated trees
```

A test that asserts on an emitted *string* proves the generator produced the characters you
expected. It does not prove a consumer can build. Before this was enforced, three defects that
emitted uncompilable C# passed every generator test and shipped:

- named binding attributes emitted a double-quoted string literal
- handlers with metadata but no parameters put the metadata array in the parameters slot
- `[FromHeader]` called `Get` on a plain dictionary

Each broke the build of any project using the feature. Integration tests caught them, afterwards.

Asserting on emitted source is still useful — for *how* something was generated. It is never a
substitute for `AssertNoErrors`.

## 2. What each kind of test is for

| Kind | Answers | Lives in |
|---|---|---|
| Unit | Does this class do what it says, including at its edges? | `<Assembly>.Tests` |
| Generator | Does the emitted code compile, and say what it should? | `<Generator>.Tests` |
| Conformance | Does every implementation of this interface behave alike? | `Hardened.Requests.Testing` |
| Integration | Does the real pipeline, end to end, produce the right result? | `IntegrationTests/**` |

The Web integration suite also drives a client Kiota generated from the application's exported
document, in `GeneratedClientTests` and `KiotaReturnsTests`, with the fixture that generates it in
`Hardened.IntegrationTests.WebApp.SUT.Client`, and a Refit interface over the same routes in
`RefitReturnsTests`. A test that needs a generated client copies that shape: `[assembly:
KiotaTesting]` or `[assembly: RefitTesting]` in `Bootstrap.cs`, the client as a parameter, and
`Returns<T>()` naming the response type the contract declares - `Created<Todo>`,
`NotFound<Problem>`, `NoContent` - which is the status, the body type and the headers in one word.
`Assert.ThrowsAsync` in the client's own exception types and `LastResponse` remain for what a
response type cannot say. `docs/client-testing.md` has the two packages.

Prefer the cheapest kind that can actually fail for the reason you care about. A routing bug is a
unit test; "the handler receives what the request carried" is an integration test, because that
answer depends on binding, filters and serialisation agreeing with each other.

**Do not unit-test through a mock what an integration test could assert for real.** Asserting an
item shape against a mocked DynamoDB only confirms the test agrees with itself — that is what
`[LocalDynamoDb]` exists for.

## 3. Naming

Name the behaviour, not the method under test.

```csharp
✔  public void NullReturnFromAGetIs404()
✔  public void OverlappingRoutesBindTheirOwnTokenNames()
✔  public void PartialBatchResponseNamesOnlyTheFailedMessage()

✘  public void TestNullHandler()
✘  public void GetOrder_Test2()
✘  public void HandlerInfoWorks()
```

The name is what a reader sees when CI is red at 2am. `NullReturnFromAGetIs404` tells them what
broke; `TestNullHandler` sends them to the source.

One behaviour per test. A test that needs "and" in its name is usually two tests — unless the
conjunction *is* the behaviour, as in `AllBindingSourcesCombineInASingleHandler`.

## 4. A test that encodes a past defect says so

With the date, and what shipped broken.

```csharp
/// <summary>
/// The named form of a binding attribute. This is what emitted a double-quoted string literal
/// before the generator fix, producing code that could not compile.
/// </summary>
[Fact]
public void NamedQueryStringBindingCompiles() { … }
```

This is the difference between a test someone deletes during a refactor and one they leave alone.
See `ParameterBindingTests`, `HttpMethodTests` and `OverlappingRouteTokenNamesTests` for the house
style.

## 5. Assert the observable outcome

For batch handlers that means the identifiers, not the count:

```csharp
✔  Assert.Equal("1", Assert.Single(response.BatchItemFailures).ItemIdentifier);
✘  Assert.Single(response.BatchItemFailures);
```

The count being right while the identifiers are wrong is the actual failure mode — every message
redelivers while the poison one is deleted. A test that only counts passes through it.

## 6. Report contradictions, do not encode them

If a documented behaviour does not exist, say so. Do not write a test that asserts the bug.

The status properties on `[Get]` and `[Put]` — `SuccessStatus`, `NullReturnStatus` and the rest —
are declared and never read by the web generator. The right response was to document that and
raise it as a decision, not to write `Assert.Equal(200, attribute.SuccessStatus)` and call it
covered.

## 7. Branch coverage is the real target

Several assemblies sit at half line coverage with a fifth of their branches taken — the signature
of tests that walk the happy path and never take a conditional. When choosing what to write next,
prefer the untaken branch over the untouched file.

Table-driven tests move this fastest:

```csharp
[Theory]
[InlineData("Get")] [InlineData("Post")] [InlineData("Put")]
[InlineData("Delete")] [InlineData("Patch")]
public void EveryVerbCompiles(string verb) { … }
```

## 8. Raise your floor in the same change

`coverage-baseline.json` records the coverage each assembly must not fall below. CI fails on a
regression. When your work raises coverage:

```bash
python3 scripts/coverage-gate.py --summary coverage-report/Summary.json --update
```

Review the diff, commit it with your tests. The ratchet only works if it is turned. Never run
`--update` in CI — a workflow that re-baselines cannot detect a regression.

Lowering a floor is allowed, and requires a reason in the commit message. Deleting dead code is a
good reason. "The test was slow" is not.

## 9. The public surface is a contract

`Hardened.PublicApi.Tests` holds an approved file per shipped assembly. A diff there means the
shipped contract changed.

```bash
APPROVE_PUBLIC_API=1 dotnet test src/PublicApi/Hardened.PublicApi.Tests
```

Read the diff before approving. `[Delete]` shipped for three years as an `internal` class nobody
could apply, while the README advertised it — this test exists so that cannot recur.

## 10. Skipping

There is no `Skip` in CI. The workflow fails on any skipped test, and on any test silently dropped
for a duplicate ID.

A repro pinned to a defect belongs in the same change as the fix, so it runs. If a test genuinely
cannot run on a runner, delete it and open an issue — a permanent `Skip` is a comment that costs a
test run.

Tests needing Docker (`[LocalDynamoDb]`, Testcontainers) are the exception locally: they fail
rather than skip on a machine without a daemon, which is deliberate. A silently skipped data test
is worse than a failing one.

## 11. Ownership while the workstreams run

Each workstream owns a disjoint set of directories. Do not add tests to a project another
workstream owns — see `TESTING-PLAN.md` §6 for the map.

Every test project is already created and registered in the solution. If you find yourself editing
`.sln`, `Directory.Build.props` or `coverage.runsettings`, stop: those are shared, and ten agents
editing them conflict. `coverage-baseline.json` is the one shared file you are expected to touch,
and only to raise your own assembly's floor.

## 12. xunit version, and the runner packages

New test projects use **xunit.v3**, and reference `Hardened.Shared.Testing.xUnit` for
`[HardenedTest]`.

Not a style preference. `Hardened.Shared.Testing.xUnit` depends on `DependencyModules.xUnit`,
which brings xunit.v3; referencing it alongside xunit 2.9 makes every `Fact` and `Assert`
ambiguous (CS0433). Two older `Hardened.Amz` projects still use 2.9 and are fine as long as they
never touch the runner package.

`Hardened.Shared.Testing` itself names no runner, and neither do `Hardened.Web.Testing`,
`Hardened.Kiota.Testing` and `Hardened.Refit.Testing`: they read the running test through
`CurrentTest`, which the runner package installs when it loads. An NUnit project references
`Hardened.Shared.Testing.NUnit` instead, and its `[HardenedTest]` is the same name in the same
namespace over `DependencyModules.NUnit`. `Hardened.Shared.Testing.NUnit.Tests` and
`Hardened.IntegrationTests.WebApp.SUT.NUnitTests` are the two NUnit projects in this repository,
and the only ones: they exist to hold the harness to reading the same under both runners, not as a
second place to put tests. `Hardened.Requests.Testing` still carries xunit for its conformance
suites, which are xUnit test classes by design, so an NUnit project referencing the web harness
sees xunit assemblies it never uses.

A shipped library must never depend on `Microsoft.NET.Test.Sdk`. `Hardened.Amz.Function.Lambda.Testing`
did, alongside both xunit generations, which made the package impossible for a consumer to use.
