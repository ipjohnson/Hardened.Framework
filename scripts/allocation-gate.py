#!/usr/bin/env python3
"""
Fail the build when a benchmark allocates more per operation than its recorded baseline.

A ceiling, not a ratchet, and the sibling of coverage-gate.py in every respect but direction.
Each baseline starts at the bytes that benchmark already allocated, so nothing regresses on day
one, and an improvement is reported rather than recorded - a floor or a ceiling is moved
deliberately or not at all.

Why allocation and not time
---------------------------

Hosted runners are shared vCPUs with no pinning, and benchmarks.yaml says at length why a
threshold on nanoseconds would flake constantly there. That argument does not reach this number.
BenchmarkDotNet's BytesAllocatedPerOperation is a count, not a duration: it does not move with
how fast the machine is or what the neighbouring container is doing.

It is also the column that carries the signal. The three allocations removed in #335 came to 56
bytes per request, and every one of the fifteen pipeline measurements moved by exactly the
predicted amount. The timings for that same change moved by less than the run-to-run variance on
a quiet laptop running the full DefaultJob. What a gate on time would have reported is noise;
what a gate on bytes reports is the change.

Measured on 2026-09-11, the same fifteen benchmarks read identically under --job short and under
the default job, to the rounding the markdown exporter applies. Iteration count does not move it
either.

What does move it
-----------------

The runtime. A patch bump to .NET 8 can change what the BCL allocates underneath the pipeline,
and that is a legitimate re-baseline rather than a regression to chase. It is also why the
workflow pins the SDK rather than floating it: a benchmark is a measurement, and the runtime it
was taken on is part of the result. If every entry moves by the same handful of bytes in the same
direction on a run that changed no pipeline code, look at the runtime version in the step summary
before looking at the diff.

Take the reading from CI rather than from a laptop, and not only for the reason coverage-gate.py
gives about what is checked out beside this repository. The platform moves it too. Seeding this
baseline on 2026-09-11, twenty of the twenty-two benchmarks read the same byte for byte on an
arm64 macOS laptop and on the x64 Linux runner. The other two were POST sum, which reads a JSON
body, and it allocated 32 bytes more on the runner in all three harnesses - the same 32 in each,
so a platform difference in the read path rather than noise.

That asymmetry is harmless in the direction it runs: a laptop that allocates less reports an
improvement, and improvements do not fail. A laptop that allocated more would fail against a
baseline it was never measured against, which is one more reason this gate belongs in CI.

Usage:
    python3 scripts/allocation-gate.py --results BenchmarkDotNet.Artifacts/results
    python3 scripts/allocation-gate.py --results <dir> --update

--update rewrites the baseline from the run it was given. Never run it in CI: a workflow that
re-baselines cannot detect a regression.
"""

from __future__ import annotations

import argparse
import json
import os
import pathlib
import sys

# BytesAllocatedPerOperation is a total divided by an operation count, so the last byte or two is
# rounding rather than measurement. This is deliberately smaller than any single allocation the
# gate exists to catch - the array enumerator it was built for is 32 bytes and a boxed int is 24 -
# so a tolerance that absorbs the rounding cannot absorb a regression.
TOLERANCE_BYTES = 16

REPO_ROOT = pathlib.Path(__file__).resolve().parent.parent
DEFAULT_BASELINE = REPO_ROOT / "build" / "allocation-baseline.json"


def read_results(directory: pathlib.Path) -> dict[str, int]:
    """Bytes per operation for every benchmark, from BenchmarkDotNet's JSON exporter."""
    if not directory.is_dir():
        sys.exit(
            f"No results directory at {directory}.\n"
            "  Produce one with:\n"
            "    dotnet run --project src/Benchmarks/Hardened.Benchmarks -c Release -- \\\n"
            '      --no-verify --filter "*PipelineBenchmarks*" --job short --exporters json'
        )

    reports = sorted(directory.glob("*-report-full-compressed.json"))

    if not reports:
        sys.exit(
            f"No JSON reports in {directory}.\n"
            "  The run needs --exporters json; the markdown and CSV exporters round to two\n"
            "  decimal places of a kilobyte, which is coarser than what this gate measures."
        )

    measured: dict[str, int] = {}

    for report in reports:
        document = json.loads(report.read_text())

        for benchmark in document.get("Benchmarks", []):
            memory = benchmark.get("Memory")

            if memory is None or "BytesAllocatedPerOperation" not in memory:
                # A benchmark class without [MemoryDiagnoser] reports no Memory block at all.
                # Skipped rather than treated as zero, which would gate it at an allocation it was
                # never measured for.
                continue

            measured[benchmark["FullName"]] = int(memory["BytesAllocatedPerOperation"])

    return measured


def write_summary(lines: list[str]) -> None:
    """Append to the run summary, when there is one to append to."""
    path = os.environ.get("GITHUB_STEP_SUMMARY")

    if not path:
        return

    with open(path, "a", encoding="utf-8") as handle:
        handle.write("\n".join(lines) + "\n")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--results", required=True, type=pathlib.Path)
    parser.add_argument("--baseline", default=DEFAULT_BASELINE, type=pathlib.Path)
    parser.add_argument("--update", action="store_true")
    args = parser.parse_args()

    measured = read_results(args.results)

    if not measured:
        sys.exit("No benchmark in the results reported allocation. Is [MemoryDiagnoser] missing?")

    if args.update:
        baseline = {name: measured[name] for name in sorted(measured)}
        args.baseline.write_text(json.dumps(baseline, indent=2) + "\n")
        print(f"Wrote {len(baseline)} benchmark baselines to {args.baseline}")

        return 0

    if not args.baseline.exists():
        print("::error::No allocation baseline. Commit this, taken from a CI run:")
        print(json.dumps({name: measured[name] for name in sorted(measured)}, indent=2))

        return 1

    baseline = json.loads(args.baseline.read_text())

    regressions: list[str] = []
    improvements: list[str] = []
    unmeasured: list[str] = []

    for name, ceiling in sorted(baseline.items()):
        if name not in measured:
            # A benchmark that vanishes from the report is not passing - it is not being measured.
            # Renaming one, or dropping it from the filter the workflow runs, would otherwise read
            # as success.
            unmeasured.append(name)
            continue

        actual = measured[name]
        ceiling = int(ceiling)

        if actual > ceiling + TOLERANCE_BYTES:
            regressions.append(
                f"  {name}: {actual} B is above the {ceiling} B baseline (+{actual - ceiling})"
            )
        elif actual < ceiling - TOLERANCE_BYTES:
            improvements.append(f"  {name}: {actual} B (baseline {ceiling} B, -{ceiling - actual})")

    new_benchmarks = sorted(set(measured) - set(baseline))

    if improvements:
        # Reported, not acted on. See the module docstring, and coverage-gate.py for the run that
        # taught the lesson: a gate that re-baselines itself on every green run cannot fail.
        print("Allocation improved:")
        print("\n".join(improvements))
        print()

    if new_benchmarks:
        print("Benchmarks with no baseline (add one with --update, from a CI run):")
        print("\n".join(f"  {name}: {measured[name]} B" for name in new_benchmarks))
        print()

    if unmeasured:
        print("::error::Benchmarks in the baseline that this run did not report:")
        print("\n".join(f"  {name}" for name in unmeasured))
        print("  Either they were renamed, or the workflow's filter no longer selects them.\n")

    if regressions:
        print("::error::Allocation regressed:")
        print("\n".join(regressions))
        print(
            "\n  Find what is allocating, or - if the increase is deliberate and worth it - "
            "re-baseline with --update and say why in the commit message."
        )

    write_summary(
        ["## Allocation", "", "| Benchmark | Bytes | Baseline |", "| --- | ---: | ---: |"]
        + [
            f"| {name.split('.')[-1]} | {measured[name]} | "
            f"{baseline.get(name, '-')} |"
            for name in sorted(measured)
        ]
    )

    if regressions or unmeasured:
        return 1

    print(f"Allocation gate passed: {len(baseline)} benchmarks at or below baseline.")

    return 0


if __name__ == "__main__":
    sys.exit(main())
