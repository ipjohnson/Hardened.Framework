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

# A scaffold pinning a preview cannot restore from nuget.org, because a preview is published to
# GitHub Packages alone - so `dotnet new` wrote a project that did not build, with NU1101 and
# nothing saying which feed was missing. This does not add the feed, it names it: an authenticated
# source with no credentials answers 401, and a 401 fails the whole restore rather than the one
# package, which would be worse than what it replaces. Commented out, with what to do, so the
# restore fails exactly as it did and the file says why.
#
# The credentials are environment variables rather than a token written into the project. NuGet
# expands %VAR% in a config value when it restores, so nothing lands on disk.
#
# A release is on nuget.org and the marker comes out, leaving the file the two lines it always was.
PREVIEW_MARKER = "<!--#PREVIEW-SOURCE#-->\n"

PREVIEW_SOURCE = """  <!-- This project pins a preview of Hardened, and previews are published to GitHub Packages
       rather than nuget.org. To restore it, uncomment the source and the credentials below and
       set GITHUB_USERNAME and a GITHUB_TOKEN holding read:packages. NuGet expands the variables
       when it restores, so no token is written into this file.

       Delete all of this once the project pins a released version.

  <packageSources>
    <add key="hardened-previews" value="https://nuget.pkg.github.com/ipjohnson/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <hardened-previews>
      <add key="Username" value="%GITHUB_USERNAME%" />
      <add key="ClearTextPassword" value="%GITHUB_TOKEN%" />
    </hardened-previews>
  </packageSourceCredentials>
  -->
"""


def preview_source(version):
    """The feed a preview scaffold needs, or nothing for a released one."""
    return PREVIEW_SOURCE if "-preview" in version else ""

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
markers = 0

for root, _, files in os.walk(destination):
    for name in files:
        path = os.path.join(root, name)

        try:
            with open(path, encoding="utf-8") as handle:
                content = handle.read()
        except (UnicodeDecodeError, OSError):
            continue

        if PREVIEW_MARKER in content:
            content = content.replace(PREVIEW_MARKER, preview_source(sys.argv[3]))
            markers += 1
        elif not any(token in content for token in TOKENS):
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

# One per template. Zero means the marker was renamed or dropped, and a preview scaffold would go
# back to restoring from a feed that does not have it.
if markers == 0:
    print(f"stage-templates: no nuget.config carried {PREVIEW_MARKER.strip()}; a preview scaffold "
          "would not see the feed its packages are on", file=sys.stderr)
    sys.exit(1)

print(f"stage-templates: resolved the preview source in {markers} nuget.config file(s)")
