# Testing conventions

The conventions are shared across the ecosystem and maintained in one place:

**[Hardened.Framework/docs/testing-conventions.md](https://github.com/ipjohnson/Hardened.Framework/blob/main/docs/testing-conventions.md)**

They apply to this repository unchanged. Two points matter more here than in the framework:

- **Partial batch responses are asserted by identifier, not count.** The count being right while
  the identifiers are wrong is the actual failure mode — every message redelivers while the poison
  one is deleted.
- **Docker-dependent tests fail rather than skip** on a machine without a daemon. A silently
  skipped data test is worse than a failing one.

`scripts/coverage-gate.py` and `coverage-baseline.json` work exactly as they do in the framework.

The workstream plan this repository's test work is scoped by lives alongside them:
**[Hardened.Framework/docs/TESTING-PLAN.md](https://github.com/ipjohnson/Hardened.Framework/blob/main/docs/TESTING-PLAN.md)**
— `aws-batch`, `aws-web` and `aws-clients-cdk` are the workstreams that apply here.
