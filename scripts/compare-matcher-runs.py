#!/usr/bin/env python3
"""Pairs two BenchmarkDotNet result tables by (method, scale, scenario) and prints the change.

Usage: compare-matcher-runs.py before.txt after.txt
"""

import re
import sys


def parse(path):
    rows = {}

    for line in open(path):
        if not line.startswith("|") or "Mean" in line or set(line.strip()) <= set("|-: "):
            continue

        cells = [c.strip() for c in line.strip().strip("|").split("|")]

        if len(cells) < 11 or not cells[0]:
            continue

        method, scale, scenario = cells[0], cells[1], cells[2]

        mean = to_ns(cells[3])
        allocated = to_bytes(cells[-2])

        if mean is None:
            continue

        rows[(method, scale, scenario)] = (mean, allocated)

    return rows


def to_ns(cell):
    match = re.match(r"([\d,]+\.?\d*)\s*(ns|us|ms)", cell)

    if not match:
        return None

    value = float(match.group(1).replace(",", ""))

    return value * {"ns": 1, "us": 1000, "ms": 1000000}[match.group(2)]


def to_bytes(cell):
    cell = cell.strip()

    if cell in ("-", "NA", ""):
        return 0

    match = re.match(r"([\d,]+\.?\d*)\s*(B|KB)", cell)

    if not match:
        return None

    value = float(match.group(1).replace(",", ""))

    return value * (1024 if match.group(2) == "KB" else 1)


def main():
    before, after = parse(sys.argv[1]), parse(sys.argv[2])

    scenarios = [
        "literal-shallow",
        "literal-deep",
        "token-terminal",
        "token-mid-short",
        "token-mid-guid",
        "token-two",
        "miss",
    ]

    for method in ("Hardened", "AspNetDfa"):
        print(f"\n{method}")
        print(f"{'scenario':<18}{'scale':>7}{'before':>12}{'after':>12}{'change':>10}{'alloc':>12}")

        for scale in ("14", "105", "504"):
            for scenario in scenarios:
                key = (method, scale, scenario)

                if key not in before or key not in after:
                    continue

                b_mean, b_alloc = before[key]
                a_mean, a_alloc = after[key]

                change = (a_mean - b_mean) / b_mean * 100
                alloc = "same" if a_alloc == b_alloc else f"{b_alloc:.0f}->{a_alloc:.0f} B"

                print(
                    f"{scenario:<18}{scale:>7}{b_mean:>11.2f}{a_mean:>12.2f}"
                    f"{change:>9.1f}%{alloc:>12}"
                )


if __name__ == "__main__":
    main()
