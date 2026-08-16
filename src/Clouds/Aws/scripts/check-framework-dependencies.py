#!/usr/bin/env python3
"""
Fail a release when a resolved Hardened.* framework package is not on nuget.org.

`dotnet pack` writes whatever version was *resolved* into the nuspec, and it does not care which
feed that version came from. This repository restores Hardened.* from GitHub Packages, which
carries previews that nuget.org never sees — so a preview in the restore graph becomes a
dependency on the published package that nobody restoring from nuget.org can satisfy.

That is not hypothetical. Hardened.Amz.Web.Lambda.Runtime 0.1.0-rc1 is on nuget.org today
declaring:

    Hardened.Requests.Abstract -> 1.0.0-preview10197

which 404s there. The package cannot be installed by anyone using nuget.org alone, and nothing
failed to produce it: the release restore had the private feed, and pack was satisfied. A version
on nuget.org can be unlisted but never removed, so this runs before packing rather than after.

Hardened.Amz.* is excluded. Those are the IDs this run is publishing — by definition they are not
on the feed yet, and the pack list already asserts them.

Usage:
    python3 scripts/check-framework-dependencies.py
    python3 scripts/check-framework-dependencies.py --root .
"""

from __future__ import annotations

import argparse
import json
import pathlib
import sys
import urllib.error
import urllib.request

REPO_ROOT = pathlib.Path(__file__).resolve().parent.parent

FEED = "https://api.nuget.org/v3-flatcontainer"

TIMEOUT_SECONDS = 30


def resolved_framework_packages(root: pathlib.Path) -> list[tuple[str, str]]:
    """Every Hardened.* package in the restore graph that this release does not itself publish."""
    found: set[tuple[str, str]] = set()

    for assets_path in root.glob("**/obj/project.assets.json"):
        with assets_path.open() as handle:
            assets = json.load(handle)

        for libraries in assets.get("targets", {}).values():
            for key in libraries:
                name, _, version = key.partition("/")
                if name.startswith("Hardened.") and not name.startswith("Hardened.Amz."):
                    found.add((name, version))

    return sorted(found)


def is_on_nuget_org(name: str, version: str) -> bool:
    lower = name.lower()
    url = f"{FEED}/{lower}/{version}/{lower}.{version}.nupkg"

    request = urllib.request.Request(url, method="HEAD")
    try:
        with urllib.request.urlopen(request, timeout=TIMEOUT_SECONDS) as response:
            return response.status == 200
    except urllib.error.HTTPError as error:
        if error.code == 404:
            return False
        # Anything else - a 5xx, a throttle - is the feed being unavailable rather than an answer
        # about this package. Treat it as fatal rather than guessing, since guessing "present"
        # publishes something unrestorable and guessing "absent" blocks a good release anyway.
        raise


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--root",
        type=pathlib.Path,
        default=REPO_ROOT,
        help="Directory to search for project.assets.json (default: repository root)",
    )
    arguments = parser.parse_args()

    packages = resolved_framework_packages(arguments.root)

    if not packages:
        print(
            "::error::No Hardened.* framework packages found in the restore graph. Either the "
            "restore did not run, or this check is looking in the wrong place."
        )
        return 1

    missing = []
    for name, version in packages:
        if is_on_nuget_org(name, version):
            print(f"  ok      {name} {version}")
        else:
            print(f"  MISSING {name} {version}")
            missing.append((name, version))

    if missing:
        for name, version in missing:
            print(
                f"::error::{name} {version} is not on nuget.org. Pack would write it into the "
                "nuspec, and the published package would be unrestorable from that feed."
            )
        print(
            "::error::Pin Hardened.* in src/Directory.Packages.props to a released version "
            "rather than a preview."
        )
        return 1

    print(f"All {len(packages)} framework dependencies are on nuget.org.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
