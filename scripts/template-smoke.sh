#!/usr/bin/env bash
#
# Build the framework and the template, install the packed template, generate a project from it,
# and prove the result restores, builds, tests and serves.
#
# The packed nupkg is installed rather than the template folder, deliberately. A template tested
# from source proves nothing about packaging, which is where the 0.8.0-rc1000 quickstart broke.
#
# Usage: scripts/template-smoke.sh [host:contract ...]
#        default: kestrel:code aspnet:code kestrel:openapi kestrel:smithy
#
# smithy is skipped unless the Smithy CLI is on PATH at the pinned version - a build without it
# fails by design (HSMT011), and that is the toolchain's problem rather than the template's.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# Unique per run, and that is not cosmetic. NuGet caches an extracted package by id and version
# in the global packages folder, so a fixed version means the second run restores the first run's
# content however many times the framework has been rebuilt since - a green smoke over stale
# packages, which is worse than no smoke at all.
VERSION="0.0.0-smoke$(date +%s)"
FEED="${TMPDIR:-/tmp}/hardened-smoke-feed"
WORK="${TMPDIR:-/tmp}/hardened-smoke-work"
SMITHY_PIN=1.73.0

COMBOS=("$@")
if [ ${#COMBOS[@]} -eq 0 ]; then
    COMBOS=(kestrel:code aspnet:code kestrel:openapi)

    if command -v smithy >/dev/null 2>&1 && [ "$(smithy --version 2>/dev/null)" = "$SMITHY_PIN" ]; then
        COMBOS+=(kestrel:smithy)
    else
        FOUND="$(command -v smithy >/dev/null 2>&1 && smithy --version || echo none)"
        echo "note: skipping the smithy contract - it needs the Smithy CLI at $SMITHY_PIN, found $FOUND"
    fi
fi

say() { printf '\n\033[1m== %s\033[0m\n' "$*"; }

rm -rf "$FEED" "$WORK"
mkdir -p "$FEED" "$WORK"

# Previous runs' packages, which are never referenced again.
find "${NUGET_PACKAGES:-$HOME/.nuget/packages}" -maxdepth 2 -type d -name '0.0.0-smoke*' \
    -exec rm -rf {} + 2>/dev/null || true

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

for COMBO in "${COMBOS[@]}"; do
    HOST="${COMBO%%:*}"
    CONTRACT="${COMBO##*:}"
    say "host: $HOST   contract: $CONTRACT"
    OUT="$WORK/$HOST-$CONTRACT"

    # --HardenedVersion is deliberately NOT passed. The template stamps the version it was
    # packed with as the default, and that default is what a real user gets - so it is what
    # needs testing. Passing it here would test the flag and leave the default unexercised.
    dotnet new hardened-web -n Smoke -o "$OUT" --host "$HOST" --contract "$CONTRACT" --skipRestore

    # The generated nuget.config names nuget.org only, which is what a real user wants. The
    # smoke run also needs the framework build that has not been published yet.
    dotnet nuget add source "$FEED" --name smoke-local --configfile "$OUT/nuget.config" >/dev/null

    ( cd "$OUT" && dotnet build -v q --nologo )
    ( cd "$OUT" && dotnet test --no-build -v q --nologo )

    # dotnet run launches the built binary as a child, so killing the wrapper leaves the
    # application holding the port. Everything below matches on the generated output directory,
    # which is unique per combination, and each probe gets its own port - without both, the
    # second probe silently talks to the first probe's process.
    serve() {
        local port="$1" env_name="$2" log="$3"
        ( cd "$OUT/src/Smoke.Host" && PORT="$port" HARDENED_ENVIRONMENT="$env_name" \
            dotnet run --no-build >"$log" 2>&1 & )

        for _ in $(seq 1 60); do
            local code
            code=$(curl -s -o /dev/null -w '%{http_code}' --max-time 1 \
                "http://localhost:$port/greeting/world" || echo 000)
            [ "$code" = "200" ] && return 0
            sleep 0.4
        done

        return 1
    }

    stop() {
        pkill -f "$OUT/src/Smoke.Host" 2>/dev/null || true
        sleep 0.5
    }

    PORT=$((5300 + RANDOM % 200))
    CODE=000
    BODY=""
    DOCS_DEV=000

    if serve "$PORT" development "$OUT/run.log"; then
        CODE=200
        BODY=$(curl -s --max-time 2 "http://localhost:$PORT/greeting/world" || true)
        DOCS_DEV=$(curl -s -o /dev/null -w '%{http_code}' --max-time 2 "http://localhost:$PORT/docs" || echo 000)
    fi

    stop

    # The same application in another environment, on its own port, to prove the reference page
    # is gated rather than simply absent.
    PROD_PORT=$((PORT + 1))
    DOCS_PROD=000

    if serve "$PROD_PORT" production "$OUT/run-prod.log"; then
        DOCS_PROD=$(curl -s -o /dev/null -w '%{http_code}' --max-time 2 "http://localhost:$PROD_PORT/docs" || echo 000)
    fi

    stop

    if [ "$CODE" = "200" ] && [ -n "$BODY" ]; then
        echo "   serving: $CODE $BODY"

        # The reference page is installed for development only, so it has to be reachable here
        # and gone under another environment. Both halves, because a gate that never opens looks
        # exactly like a gate that works.
        DOCS_DEV=$(curl -s -o /dev/null -w '%{http_code}' --max-time 2 "http://localhost:$PORT/docs" || echo 000)
        echo "   /docs  development=$DOCS_DEV  production=$DOCS_PROD"

        # Asserted for code-first only, because that is the only path with a gate. A spec-first
        # application installs its reference page through UiUrl metadata on the contract item,
        # which has no environment story at all - so its page is served in every environment,
        # and asserting otherwise here would be asserting a framework gap closed.
        if [ "$CONTRACT" = "code" ]; then
            if [ "$DOCS_DEV" != "200" ]; then
                echo "   FAILED: the reference page should be served in development"
                FAILED=1
            fi

            if [ "$DOCS_PROD" = "200" ]; then
                echo "   FAILED: the reference page reached production"
                FAILED=1
            fi
        else
            echo "   note: spec-first installs its page from contract metadata, which is not gated"
        fi
    else
        echo "   FAILED: status=$CODE body='$BODY'"
        tail -20 "$OUT/run.log" || true
        FAILED=1
    fi
done

say "host independence"
# The framework's claim is that handlers, filters, binding and routing do not change with the
# host. If that is true, only the host project may differ between two generated applications.
# Comparable only across hosts at the same contract, which is the claim being checked.
A="$WORK/kestrel-code"; B="$WORK/aspnet-code"
if [ -d "$A" ] && [ -d "$B" ]; then
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
