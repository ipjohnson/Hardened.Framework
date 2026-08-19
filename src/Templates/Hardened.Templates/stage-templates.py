#!/usr/bin/env python3
"""Stage template content for packing, with the framework version stamped in.

A template pins the Hardened version its generated projects restore, and a hardcoded one goes
stale exactly the way the RazorBlade install snippet did - four release lines behind, with
nothing to notice. The version therefore comes from the build rather than the file.

Staged into obj/ rather than rewritten in place: pack must not leave the working tree dirty,
and the token has to survive in source so the next pack can substitute it again.
"""
import os
import shutil
import sys

TOKEN = "0.0.0-DEV"

source, destination, version = sys.argv[1], sys.argv[2], sys.argv[3]

if os.path.isdir(destination):
    shutil.rmtree(destination)

# bin/ and obj/ under a template are build leftovers from someone opening it in an IDE. They
# are not content, and packing them would ship a stranger's absolute paths.
shutil.copytree(
    source,
    destination,
    ignore=shutil.ignore_patterns("bin", "obj"),
)

stamped = 0

for root, _, files in os.walk(destination):
    for name in files:
        path = os.path.join(root, name)

        try:
            with open(path, encoding="utf-8") as handle:
                content = handle.read()
        except (UnicodeDecodeError, OSError):
            continue

        if TOKEN not in content:
            continue

        with open(path, "w", encoding="utf-8") as handle:
            handle.write(content.replace(TOKEN, version))

        stamped += 1

if stamped == 0:
    print(f"stage-templates: nothing carried {TOKEN}; the version would ship unstamped",
          file=sys.stderr)
    sys.exit(1)

print(f"stage-templates: stamped {version} into {stamped} file(s)")
