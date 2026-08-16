#!/usr/bin/env python3
"""Pull the OpenAPI document out of the C# constant a generator wrote it into.

The code-first document is emitted as a string literal rather than a file, because a Roslyn
analyzer may not touch the file system. That is the right call for the generator and an awkward
one for a linter, which wants JSON on disk — so this extracts it.

Used by CI to hand the served document to Spectral. Worth doing rather than trusting a schema
check: path templating is prose in the OpenAPI specification instead of schema, so
openapi-spec-validator calls `/boards/{boardId:guid}` valid while Spectral reports it as the
path-params error it is.

    extract-openapi.py <generated-source.cs> <output.json>
"""

import json
import pathlib
import re
import sys


def extract(source: str) -> dict:
    """The first string literal in the file, unescaped back to the JSON it holds."""
    match = re.search(r'"((?:[^"\\]|\\.)*)"', source, re.S)

    if not match:
        raise SystemExit("no string literal in the generated source")

    # The literal is C#-escaped; unicode_escape reverses the escaping the generator applied.
    return json.loads(match.group(1).encode().decode("unicode_escape"))


def main() -> None:
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)

    source = pathlib.Path(sys.argv[1])

    if not source.is_file():
        raise SystemExit(f"no generated document at {source}")

    document = extract(source.read_text())

    pathlib.Path(sys.argv[2]).write_text(json.dumps(document, indent=2))

    print(f"{source.name}: {len(document.get('paths', {}))} paths -> {sys.argv[2]}")


if __name__ == "__main__":
    main()
