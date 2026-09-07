# Design notes

Written for maintainers, not for a reader of the site. `srcExclude` in `../.vitepress/config.ts`
keeps them out of the build, so a note here is a file in the repository rather than a page.

They came from three places when the repositories became one: fifteen from `Hardened.Framework`,
two from `Hardened.Amz` under `aws/`, and the plans that were already in `docs/`.

Two kinds of file live here, and the difference matters when you read one.

**Reference.** What a subsystem does and why it behaves the way it does. `AGENTS.md` links to these
by name, and they are kept current with the code.

- `testing-conventions.md`, `aws/testing-conventions.md` — what to assert, and what not to
- `generator-diagnostics.md` — every diagnostic the generators raise
- `described-authorization.md` — what a contract's `security` becomes
- `validation-usage.md`, `validation-system.md` — constraints, custom validators, the error response
- `response-caching.md`, `request-timeouts.md`, `client-testing.md`, `openapi-document.md`
- `aws/application-types.md` — the module attribute, project shape and test setup per transport

**Dated records.** A plan or an assessment, written on a day, kept because the argument in it is
still cited. The numbers and the commands in one are what was true then. `TESTING-PLAN.md` names
`Hardened.Amz.sln`, and that is correct for 2026-08-11.

- `TESTING-PLAN.md`, `TEMPLATE-PLAN.md`, `DOCS-PLAN.md`, `ENVIRONMENT-SPLIT.md`
- `bearer-principal-source.md`, `client-by-default-handoff.md`
- `openapi-remediation-plan.html`
