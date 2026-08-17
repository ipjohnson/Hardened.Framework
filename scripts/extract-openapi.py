#!/usr/bin/env python3
"""Pull the OpenAPI document out of the C# constant a generator wrote it into.

The code-first document is emitted as a string literal rather than a file, because a Roslyn
analyzer may not touch the file system. That is the right call for the generator and an awkward
one for a linter, which wants JSON on disk — so this extracts it.

Used by CI to hand the served document to Spectral. Worth doing rather than trusting a schema
check: path templating is prose in the OpenAPI specification instead of schema, so
openapi-spec-validator calls `/boards/{boardId:guid}` valid while Spectral reports it as the
path-params error it is.

    extract-openapi.py <generated-source.cs> <output.json> [--lint-as VERSION]
"""

import json
import pathlib
import re
import sys

# The newest version Spectral's oas ruleset can validate.
#
# Measured against @stoplight/spectral-cli 6.16.3: a 3.0.0 or 3.1.0 document lints clean, and a
# 3.2.0 one fails with `oas3-schema: "openapi" property must match pattern "^3\.0\.\d(-.+)?$"` -
# Spectral does not know 3.2 and falls back to the 3.0 schema for it.
#
# That is a gap in the linter rather than in the document. Kiota, which is what the client story
# points a consumer at, has read 3.2 since v1.30.0.
LINTABLE_VERSION = "3.1.0"


def extract(source: str) -> dict:
    """The first string literal in the file, unescaped back to the JSON it holds."""
    match = re.search(r'"((?:[^"\\]|\\.)*)"', source, re.S)

    if not match:
        raise SystemExit("no string literal in the generated source")

    # The literal is C#-escaped; unicode_escape reverses the escaping the generator applied.
    return json.loads(match.group(1).encode().decode("unicode_escape"))


def relabel_for_linting(document: dict) -> dict:
    """Declare a version Spectral can validate, so its other rules still run.

    Only the `openapi` field is touched, and only downward. What the lint is here for is
    structural - path-params caught route constraints leaking into paths ten times against a real
    application - and every one of those rules is version-independent. Refusing to lint at all
    because the linter cannot read the version banner would trade a real check for a cosmetic one.

    Nothing is hidden by this today: the emitter produces no 3.2-only construct yet. `itemSchema`
    is the first one, and when it lands this has to be revisited rather than extended - a 3.1
    document carrying `itemSchema` is not something Spectral should be asked to bless. See
    STREAMING-PLAN.md item 6.
    """
    declared = document.get("openapi", "")

    if declared and declared > LINTABLE_VERSION:
        document["openapi"] = LINTABLE_VERSION
        print(f"linting as OpenAPI {LINTABLE_VERSION}; the document declares {declared}")

    return document


def main() -> None:
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)

    source = pathlib.Path(sys.argv[1])

    if not source.is_file():
        raise SystemExit(f"no generated document at {source}")

    document = relabel_for_linting(extract(source.read_text()))

    pathlib.Path(sys.argv[2]).write_text(json.dumps(document, indent=2))

    print(f"{source.name}: {len(document.get('paths', {}))} paths -> {sys.argv[2]}")


if __name__ == "__main__":
    main()
