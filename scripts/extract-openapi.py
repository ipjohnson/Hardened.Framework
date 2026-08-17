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


def downgrade_for_linting(document: dict) -> dict:
    """Reduce the document to what Spectral can validate, so its rules still run.

    Two things are removed, and only when the document declares a version above what the linter
    understands. The version banner, downward. And `itemSchema`, which is the construct OpenAPI 3.2
    added for streamed responses: under a 3.1 banner Spectral rejects it as an unevaluated property,
    once per streaming operation.

    What survives is everything the lint is actually here for. path-params caught route constraints
    leaking into paths ten times against a real application; operation-tag-defined and the rest of
    oas3-schema still run over every operation, streaming ones included - their media type, path,
    parameters and tags are all still checked. The one thing Spectral does not see is the shape of a
    streamed item.

    That shape is not unchecked, it is checked by a reader that understands the version it is
    written in: OpenApiRoundTripTests parses the emitted document with Microsoft.OpenApi, and
    OpenApiReverseRoundTripTests feeds it back through the specification-first build task and
    requires the application's shape to come back out. Both know 3.2. Spectral does not, yet.

    Delete this whole function when it does.
    """
    declared = document.get("openapi", "")

    if not declared or declared <= LINTABLE_VERSION:
        return document

    document["openapi"] = LINTABLE_VERSION

    removed = 0

    for path in document.get("paths", {}).values():
        for operation in path.values():
            if not isinstance(operation, dict):
                continue

            for response in operation.get("responses", {}).values():
                for media_type in response.get("content", {}).values():
                    if isinstance(media_type, dict) and media_type.pop("itemSchema", None):
                        removed += 1

    print(
        f"linting as OpenAPI {LINTABLE_VERSION}; the document declares {declared}"
        f" ({removed} itemSchema removed, which {LINTABLE_VERSION} has no spelling for)"
    )

    return document


def main() -> None:
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)

    source = pathlib.Path(sys.argv[1])

    if not source.is_file():
        raise SystemExit(f"no generated document at {source}")

    document = downgrade_for_linting(extract(source.read_text()))

    pathlib.Path(sys.argv[2]).write_text(json.dumps(document, indent=2))

    print(f"{source.name}: {len(document.get('paths', {}))} paths -> {sys.argv[2]}")


if __name__ == "__main__":
    main()
