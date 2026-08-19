#!/usr/bin/env bash
#
# Build the framework and the template, install the packed template, generate a project from it,
# and prove the result restores, builds, tests and serves.
#
# The packed nupkg is installed rather than the template folder, deliberately. A template tested
# from source proves nothing about packaging, which is where the 0.8.0-rc1000 quickstart broke.
#
# Usage: scripts/template-smoke.sh [host ...]        default: kestrel aspnet
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="0.0.0-smoke"
FEED="${TMPDIR:-/tmp}/hardened-smoke-feed"
WORK="${TMPDIR:-/tmp}/hardened-smoke-work"
HOSTS=("${@:-}")
[ -z "${HOSTS[0]:-}" ] && HOSTS=(kestrel aspnet)

say() { printf '\n\033[1m== %s\033[0m\n' "$*"; }

rm -rf "$FEED" "$WORK"
mkdir -p "$FEED" "$WORK"

# Pack output is noisy with pre-existing NU5100/NU5128 about build tasks that deliberately sit
# outside lib/, so it is kept in a log and only shown when something actually fails.
pack() {
    local what="$1"; shift
    say "packing $what at $VERSION"
    if ! dotnet pack "$@" -c Release -o "$FEED" -p:Version="$VERSION" -v q --nologo \
            >"$WORK/pack-$what.log" 2>&1; then
        grep -E ": error" "$WORK/pack-$what.log" | head -20
        echo "   full log: $WORK/pack-$what.log"
        exit 1
    fi
}

# UseLocalValidationModules=false so a sibling checkout cannot leak a version that was never
# published into the packed dependency graph.
pack framework "$REPO/src/Hardened.Framework.sln" \
    -p:UseLocalValidationModules=false \
    -p:HardenedSmithyPinCliVersion=false

pack template "$REPO/src/Templates/Hardened.Templates/Hardened.Templates.csproj"

say "installing the packed template"
dotnet new uninstall Hardened.Templates >/dev/null 2>&1 || true
dotnet new install "$FEED/Hardened.Templates.$VERSION.nupkg"

FAILED=0

for HOST in "${HOSTS[@]}"; do
    say "host: $HOST"
    OUT="$WORK/$HOST"

    # --HardenedVersion is deliberately NOT passed. The template stamps the version it was
    # packed with as the default, and that default is what a real user gets - so it is what
    # needs testing. Passing it here would test the flag and leave the default unexercised.
    dotnet new hardened-web -n Smoke -o "$OUT" --host "$HOST" --skipRestore

    # The generated nuget.config names nuget.org only, which is what a real user wants. The
    # smoke run also needs the framework build that has not been published yet.
    dotnet nuget add source "$FEED" --name smoke-local --configfile "$OUT/nuget.config" >/dev/null

    ( cd "$OUT" && dotnet build -v q --nologo )
    ( cd "$OUT" && dotnet test --no-build -v q --nologo )

    PORT=$((5300 + RANDOM % 200))
    ( cd "$OUT/src/Smoke.Host" && PORT=$PORT dotnet run --no-build >"$OUT/run.log" 2>&1 & echo $! >"$OUT/pid" )
    PID=$(cat "$OUT/pid")

    CODE=000
    for _ in $(seq 1 60); do
        CODE=$(curl -s -o /dev/null -w '%{http_code}' --max-time 1 "http://localhost:$PORT/greeting/world" || echo 000)
        [ "$CODE" = "200" ] && break
        kill -0 "$PID" 2>/dev/null || break
        sleep 0.4
    done

    BODY=$(curl -s --max-time 2 "http://localhost:$PORT/greeting/world" || true)
    kill "$PID" 2>/dev/null || true
    wait "$PID" 2>/dev/null || true

    if [ "$CODE" = "200" ] && [ -n "$BODY" ]; then
        echo "   serving: $CODE $BODY"
    else
        echo "   FAILED: status=$CODE body='$BODY'"
        tail -20 "$OUT/run.log" || true
        FAILED=1
    fi
done

say "host independence"
# The framework's claim is that handlers, filters, binding and routing do not change with the
# host. If that is true, only the host project may differ between two generated applications.
if [ ${#HOSTS[@]} -gt 1 ]; then
    A="$WORK/${HOSTS[0]}"; B="$WORK/${HOSTS[1]}"
    # Source only. bin/ and obj/ carry absolute paths and compiler output, which differ for
    # reasons that have nothing to do with the host.
    for PART in src/Smoke tests/Smoke.Tests; do
        if diff -r -x bin -x obj "$A/$PART" "$B/$PART" >/dev/null 2>&1; then
            echo "   identical across hosts: $PART"
        else
            echo "   FAILED: $PART differs between ${HOSTS[0]} and ${HOSTS[1]}"
            diff -r -x bin -x obj "$A/$PART" "$B/$PART" | head -20
            FAILED=1
        fi
    done
fi

dotnet new uninstall Hardened.Templates >/dev/null 2>&1 || true

say "$([ $FAILED -eq 0 ] && echo 'smoke passed' || echo 'SMOKE FAILED')"
exit $FAILED
