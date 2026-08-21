#!/usr/bin/env python3
"""
Fail the build when any assembly's coverage falls below its recorded baseline.

A ratchet, not a cliff. Each baseline starts at the coverage that assembly already had, so
nothing regresses on day one, and each workstream raises its own floor as it lands. Before this
existed, CI measured coverage on every build, printed it, and enforced nothing — the numbers
could fall to zero and the build stayed green.

Usage:
    python3 scripts/coverage-gate.py --summary coverage-report/Summary.json
    python3 scripts/coverage-gate.py --summary coverage-report/Summary.json --update

--update rewrites the baseline from the current run. Never run it in CI: a workflow that
re-baselines cannot detect a regression.

Take the summary from a CI run rather than a local one. The generator assemblies compile their
dependencies from source, and which source depends on what is checked out beside this repository:
CSharpAuthor.props and ValidationModulesImpl.props switch to a sibling checkout when one exists,
so a developer with ~/CSharpAuthor or ~/ValidationModules builds assemblies whose contents differ
from the ones CI builds. A baseline written from that machine records percentages CI cannot
reproduce, and an assembly - ValidationModules.Runtime - that only exists as a project locally.

The coverage-report artifact is uploaded on every run, including a failed one - which is what
makes a run that died in a later step, such as Pack, still usable for re-baselining:

    gh run download <run-id> -n coverage-report -D /tmp/cicov
    python3 scripts/coverage-gate.py --summary /tmp/cicov/Summary.json --update

An assembly missing from the baseline is not necessarily an oversight
-------------------------------------------------------------------

This gate errors on a baseline entry no run reported, and only prints an assembly the run
reported and the baseline does not. So an assembly that never reaches the report is never gated,
and nothing says so.

Hardened.Smithy.BuildTask is in that state, and adding a baseline entry for it would make every
run fail rather than fix it. Measured 2026-08-18:

    dotnet build src/Hardened.Framework.sln -c Release
    dotnet test  src/Hardened.Framework.sln --no-build -c Release \
        --settings coverage.runsettings --collect:"Code Coverage"
        -> 20 assemblies, Hardened.Smithy.BuildTask among them

    dotnet build src/Hardened.Framework.sln -c Release -p:ContinuousIntegrationBuild=true
    (same test command)
        -> 19 assemblies, Hardened.Smithy.BuildTask absent, and two of the 23 cobertura files
           come out with no <package> element at all

CI always builds with ContinuousIntegrationBuild=true, so the assembly has never appeared in a
CI report - its own 73 tests run and pass there, and their coverage is discarded. That is roughly
8,600 lines ungated, counting the Idl.Emit and Idl.Shared sources it compiles in.

The two empty cobertura files are the thing to chase: coverlet is producing a report and putting
nothing in it, which is a collection failure rather than a merge one. Deterministic builds rewrite
source paths to /_/..., and DeterministicReport is set below, so the path mapping is the first
place to look. Until it is fixed, a missing assembly here means unmeasured, not untested.
"""

from __future__ import annotations

import argparse
import json
import pathlib
import sys

# Coverage is measured to one decimal place, and re-runs wobble slightly when a test touches a
# line conditionally. Anything smaller than this is noise, not a regression.
#
# --update records whatever the run it was given measured, so baselining off a run that happened
# to read at the top of an assembly's wobble leaves no headroom underneath and the next ordinary
# run fails. Hardened.Shared.Runtime's branch coverage moves between 85.2 and 85.8 - wider than
# this tolerance - and was briefly pinned at 85.8 that way. Where an assembly is known to wobble,
# the baseline belongs at the bottom of the range rather than at the reading in front of you.
TOLERANCE = 0.5

REPO_ROOT = pathlib.Path(__file__).resolve().parent.parent
DEFAULT_BASELINE = REPO_ROOT / "coverage-baseline.json"


def read_summary(path: pathlib.Path) -> dict[str, dict[str, float]]:
    """Per-assembly line and branch coverage from a ReportGenerator JsonSummary."""
    if not path.exists():
        sys.exit(
            f"No coverage summary at {path}.\n"
            "  Generate one with:\n"
            '    reportgenerator -reports:"coverage/**/*.cobertura.xml" '
            '-targetdir:coverage-report -reporttypes:"JsonSummary"'
        )

    document = json.loads(path.read_text())

    assemblies = {}

    for assembly in document.get("coverage", {}).get("assemblies", []):
        # branchcoverage is absent for an assembly with no branches at all. Treat that as 100:
        # there is nothing to cover, so it can never regress.
        assemblies[assembly["name"]] = {
            "line": float(assembly.get("coverage") or 0.0),
            "branch": float(assembly["branchcoverage"])
            if assembly.get("branchcoverage") is not None
            else 100.0,
        }

    return assemblies


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--summary", required=True, type=pathlib.Path)
    parser.add_argument("--baseline", default=DEFAULT_BASELINE, type=pathlib.Path)
    parser.add_argument("--update", action="store_true")
    args = parser.parse_args()

    measured = read_summary(args.summary)

    if args.update:
        baseline = {
            name: {"line": round(values["line"], 1), "branch": round(values["branch"], 1)}
            for name, values in sorted(measured.items())
        }
        args.baseline.write_text(json.dumps(baseline, indent=2) + "\n")
        print(f"Wrote {len(baseline)} assembly baselines to {args.baseline}")

        return 0

    if not args.baseline.exists():
        sys.exit(
            f"No baseline at {args.baseline}. Create one with --update, review it, and commit it."
        )

    baseline = json.loads(args.baseline.read_text())

    regressions: list[str] = []
    improvements: list[str] = []
    unmeasured: list[str] = []

    for name, floors in sorted(baseline.items()):
        if name not in measured:
            # An assembly that vanishes from the report is not passing — it is not being measured.
            # Deleting its only test project would otherwise read as success.
            unmeasured.append(name)
            continue

        for metric in ("line", "branch"):
            actual = measured[name][metric]
            floor = float(floors[metric])

            if actual < floor - TOLERANCE:
                regressions.append(
                    f"  {name} {metric}: {actual:.1f}% is below the {floor:.1f}% baseline "
                    f"(-{floor - actual:.1f})"
                )
            elif actual > floor + TOLERANCE:
                improvements.append(f"  {name} {metric}: {actual:.1f}% (baseline {floor:.1f}%)")

    new_assemblies = sorted(set(measured) - set(baseline))

    if improvements:
        print("Coverage improved:")
        print("\n".join(improvements))
        print("\nRaise the floor: python3 scripts/coverage-gate.py --summary "
              f"{args.summary} --update\n")

    if new_assemblies:
        print("Assemblies with no baseline (add one with --update):")
        print("\n".join(f"  {name}" for name in new_assemblies))
        print()

    if unmeasured:
        print("::error::Assemblies in the baseline that no coverage run reported:")
        print("\n".join(f"  {name}" for name in unmeasured))
        print("  Either their tests were removed, or the assembly is no longer built.\n")

    if regressions:
        print("::error::Coverage regressed:")
        print("\n".join(regressions))
        print(
            "\n  Add tests, or — if the drop is deliberate, such as deleting dead code — "
            "re-baseline with --update and say why in the commit message."
        )

    if regressions or unmeasured:
        return 1

    print(f"Coverage gate passed: {len(baseline)} assemblies at or above baseline.")

    return 0


if __name__ == "__main__":
    sys.exit(main())
