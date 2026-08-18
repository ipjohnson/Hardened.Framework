#!/usr/bin/env python3
"""
Fail the release when a project that produces a package is not in release.yaml's pack list.

release.yaml packs an explicit list of projects rather than the solution, because packing the
solution claims a public id for anything merely missing IsPackable=false - and on nuget.org an id,
once claimed, can be unlisted but never removed. The list is the right shape and it has one
failure mode: nothing notices a project that was never added to it.

That is not hypothetical. The OpenAPI generator was split into a front end (an MSBuild task that
reads a document) and a shared Roslyn generator, Hardened.Idl.SourceGenerator, which every
description language front end depends on. The new project was never added here, so 0.6.0-rc1000
published Hardened.OpenApi.SourceGenerator carrying build/ and tasks/ and no analyzers/ at all,
naming a dependency that exists on no feed. It restored, it built, and it generated nothing.

The count in release.yaml's "Verify every expected package was produced" step could not catch it:
a new project is not a project dropped from a list, so the list and the number still agreed. That
step guards against losing a package. This one guards against never having added it.

Usage:
    python3 scripts/check-pack-list.py
    python3 scripts/check-pack-list.py --workflow .github/workflows/release.yaml

A project counts as publishable when its csproj sets <IsPackable>true</IsPackable> outright. That
is the marker every published project here carries, and reading it rather than inferring packability
keeps test projects out of the answer without a second rule: Microsoft.NET.Test.Sdk sets
IsPackable=false for them, in the SDK, where a text scan cannot see it.

A project deliberately left unpublished goes in EXCLUDED below with the reason - one place, stated
once, rather than an omission that reads exactly like an oversight.
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

# Projects that set IsPackable=true and are still not published on their own. The reason belongs
# here rather than in a commit message, because the next person to run this will be looking at the
# failure it prints, not at git log.
EXCLUDED = {
    "src/SourceGenerators/Hardened.DependencyModules.SourceGenerator/Hardened.DependencyModules.SourceGenerator.csproj": (
        "Ships embedded in Hardened.Library.SourceGenerator's analyzers/dotnet/cs. Both projects "
        "compile CSharpAuthor in from source and an assembly reference between them collides on "
        "CSharpAuthor.TypeExtensions, so a nuspec dependency is not available. Published "
        "standalone as well - as v0.1.0-rc1 did - a consumer referencing both packages loads the "
        "generator twice and both copies emit the module onto the same partial class, failing "
        "with CS0111/CS8646."
    ),
}

IS_PACKABLE_TRUE = re.compile(r"<IsPackable>\s*true\s*</IsPackable>", re.IGNORECASE)
PACK_LIST_ENTRY = re.compile(r"src/[A-Za-z0-9./_-]+\.csproj")


def packable_projects(root: Path) -> set[str]:
    found = set()
    for csproj in (root / "src").rglob("*.csproj"):
        if IS_PACKABLE_TRUE.search(csproj.read_text(encoding="utf-8")):
            found.add(csproj.relative_to(root).as_posix())
    return found


def pack_list(workflow: Path) -> set[str]:
    return set(PACK_LIST_ENTRY.findall(workflow.read_text(encoding="utf-8")))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--workflow",
        default=".github/workflows/release.yaml",
        help="Workflow whose pack list is checked (default: %(default)s)",
    )
    parser.add_argument(
        "--root",
        default=".",
        help="Repository root (default: %(default)s)",
    )
    args = parser.parse_args()

    root = Path(args.root).resolve()
    workflow = root / args.workflow
    if not workflow.is_file():
        print(f"::error::No workflow at {workflow}")
        return 1

    packed = pack_list(workflow)
    packable = packable_projects(root)

    failed = False

    missing = sorted(packable - packed - set(EXCLUDED))
    if missing:
        failed = True
        for project in missing:
            print(
                f"::error::{project} sets IsPackable=true and is not packed by "
                f"{args.workflow}. Add it to the pack list and raise EXPECTED, or add it to "
                f"EXCLUDED in this script with the reason it stays unpublished."
            )

    # A path that no longer exists packs nothing and fails the count with no clue as to which
    # entry went stale.
    stale = sorted(entry for entry in packed if not (root / entry).is_file())
    if stale:
        failed = True
        for entry in stale:
            print(f"::error::{args.workflow} packs {entry}, which does not exist.")

    # An exclusion for a project that is no longer packable, or no longer there at all, is a note
    # about a decision that has already been unwound.
    for project, reason in sorted(EXCLUDED.items()):
        if not (root / project).is_file():
            failed = True
            print(f"::error::EXCLUDED names {project}, which does not exist. Remove the entry.")
        elif project in packed:
            failed = True
            print(
                f"::error::{project} is both EXCLUDED here and packed by {args.workflow}. "
                f"One of the two is wrong. The exclusion says: {reason}"
            )

    if failed:
        return 1

    print(f"{len(packed)} projects packed, {len(EXCLUDED)} deliberately excluded, none missed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
