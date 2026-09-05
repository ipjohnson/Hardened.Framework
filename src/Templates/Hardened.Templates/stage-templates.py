#!/usr/bin/env python3
"""Stage template content for packing, with the framework version stamped in.

A template pins the Hardened version its generated projects restore, and a hardcoded one goes
stale exactly the way the RazorBlade install snippet did - four release lines behind, with
nothing to notice. The version therefore comes from the build rather than the file. The
DependencyModules version the test project's mock package pins is stamped the same way, from
the same property the framework builds against.

Staged into obj/ rather than rewritten in place: pack must not leave the working tree dirty,
and the token has to survive in source so the next pack can substitute it again.
"""
import os
import shutil
import sys

TOKENS = {
    "0.0.0-DEV": sys.argv[3],
    "0.0.0-DEPENDENCYMODULES-VERSION": sys.argv[4],
}

source, destination = sys.argv[1], sys.argv[2]

if os.path.isdir(destination):
    shutil.rmtree(destination)

# bin/ and obj/ under a template are build leftovers from someone opening it in an IDE. They
# are not content, and packing them would ship a stranger's absolute paths.
shutil.copytree(
    source,
    destination,
    ignore=shutil.ignore_patterns("bin", "obj"),
)

stamped = {token: 0 for token in TOKENS}

for root, _, files in os.walk(destination):
    for name in files:
        path = os.path.join(root, name)

        try:
            with open(path, encoding="utf-8") as handle:
                content = handle.read()
        except (UnicodeDecodeError, OSError):
            continue

        if not any(token in content for token in TOKENS):
            continue

        for token, version in TOKENS.items():
            if token in content:
                content = content.replace(token, version)
                stamped[token] += 1

        with open(path, "w", encoding="utf-8") as handle:
            handle.write(content)

for token, version in TOKENS.items():
    if stamped[token] == 0:
        print(f"stage-templates: nothing carried {token}; the version would ship unstamped",
              file=sys.stderr)
        sys.exit(1)

    print(f"stage-templates: stamped {version} into {stamped[token]} file(s)")
