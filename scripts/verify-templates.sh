#!/usr/bin/env bash
#
# Verify the templates: build the framework, pack it and them, install the packed template,
# generate a project from every supported combination of all three templates, and prove each one
# restores, builds, tests and - where it serves HTTP - answers. This is the gate a release runs.
#
# The packed nupkg is installed rather than the template folder, deliberately. A template tested
# from source proves nothing about packaging, which is where the 0.8.0-rc1000 quickstart broke.
#
# Usage: scripts/verify-templates.sh [host:contract ...]
#        default: kestrel:code aspnet:code kestrel:openapi kestrel:smithy
#
# smithy is skipped unless the Smithy CLI is on PATH at the pinned version - a build without it
# fails by design (HSMT011), and that is the toolchain's problem rather than the template's.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# Unique per run, and that is not cosmetic. NuGet caches an extracted package by id and version
# in the global packages folder, so a fixed version means the second run restores the first run's
# content however many times the framework has been rebuilt since - a green run over stale
# packages, which is worse than no run at all.
VERSION="0.0.0-verify$(date +%s)"
FEED="${TMPDIR:-/tmp}/hardened-template-feed"
WORK="${TMPDIR:-/tmp}/hardened-template-work"
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
find "${NUGET_PACKAGES:-$HOME/.nuget/packages}" -maxdepth 2 -type d -name '0.0.0-verify*' \
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

# Built before packing, deliberately. The OpenAPI and Smithy packages include their MSBuild task
# assemblies by path out of another project's bin, and pack alone does not reliably put them
# there - so the package shipped a targets file pointing at a tasks/ folder that did not exist,
# and the first spec-first build failed with MSB4062. It passed locally only because the tree had
# been built by hand first.
say "building the framework"
if ! dotnet build "$REPO/src/Hardened.Framework.sln" -c Release \
        -p:HardenedSmithyPinCliVersion=false -v q --nologo >"$WORK/build.log" 2>&1; then
    grep -E ": error" "$WORK/build.log" | head -20
    echo "   full log: $WORK/build.log"
    exit 1
fi

# UseLocalValidationModules=false so a sibling checkout cannot leak a version that was never
# published into the packed dependency graph.
pack framework "$REPO/src/Hardened.Framework.sln" \
    -p:UseLocalValidationModules=false \
    -p:HardenedSmithyPinCliVersion=false

pack template "$REPO/src/Templates/Hardened.Templates/Hardened.Templates.csproj"

say "installing the packed template"
# Both spellings: a folder install from the inner loop registers under its path rather than the
# package id, and it shadows the packed one with the same template identity - so the verification would
# silently exercise the working tree instead of the artifact.
dotnet new uninstall Hardened.Templates >/dev/null 2>&1 || true
for TEMPLATE_DIR in "$REPO"/src/Templates/Hardened.Templates/templates/*/; do
    dotnet new uninstall "${TEMPLATE_DIR%/}" >/dev/null 2>&1 || true
done
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
    dotnet new hardened-web -n Sample -o "$OUT" --host "$HOST" --contract "$CONTRACT" --skip-restore

    # The generated nuget.config names nuget.org only, which is what a real user wants. The
    # verification run also needs the framework build that has not been published yet.
    dotnet nuget add source "$FEED" --name template-verify-local --configfile "$OUT/nuget.config" >/dev/null

    ( cd "$OUT" && dotnet build -v q --nologo )
    ( cd "$OUT" && dotnet test --no-build -v q --nologo )

    # dotnet run launches the built binary as a child, so killing the wrapper leaves the
    # application holding the port. Everything below matches on the generated output directory,
    # which is unique per combination, and each probe gets its own port - without both, the
    # second probe silently talks to the first probe's process.
    serve() {
        local port="$1" env_name="$2" log="$3"
        ( cd "$OUT/src/Sample.Host" && PORT="$port" HARDENED_ENVIRONMENT="$env_name" \
            dotnet run --no-build >"$log" 2>&1 & )

        for _ in $(seq 1 60); do
            local code
            code=$(curl -s -o /dev/null -w '%{http_code}' --max-time 1 \
                "http://localhost:$port/greeting/world" || true)
            [ "$code" = "200" ] && return 0
            sleep 0.4
        done

        return 1
    }

    stop() {
        pkill -f "$OUT/src/Sample.Host" 2>/dev/null || true
        sleep 0.5
    }

    # Retried rather than asked once. A 000 from a server still warming up is indistinguishable
    # from a 404 by a gate, and the difference decides whether this script fails the build.
    status_of() {
        local url="$1" code=000

        for _ in $(seq 1 10); do
            code=$(curl -s -o /dev/null -w '%{http_code}' --max-time 5 "$url" || true)
            [ "$code" != "000" ] && break
            sleep 0.5
        done

        echo "$code"
    }

    PORT=$((5300 + RANDOM % 200))
    CODE=000
    BODY=""
    DOCS_DEV=000

    if serve "$PORT" development "$OUT/run.log"; then
        CODE=200
        BODY=$(curl -s --max-time 2 "http://localhost:$PORT/greeting/world" || true)
        DOCS_DEV=$(status_of "http://localhost:$PORT/docs")
    fi

    stop

    # The same application in another environment, on its own port, to prove the reference page
    # is gated rather than simply absent.
    PROD_PORT=$((PORT + 1))
    DOCS_PROD=000

    if serve "$PROD_PORT" production "$OUT/run-prod.log"; then
        DOCS_PROD=$(status_of "http://localhost:$PROD_PORT/docs")
    fi

    stop

    if [ "$CODE" = "200" ] && [ -n "$BODY" ]; then
        echo "   serving: $CODE $BODY"

        # Both halves, because a gate that never opens looks exactly like a gate that works.
        # Both were probed while their server was up; re-asking here would ask a dead port.
        echo "   /docs  development=$DOCS_DEV  production=$DOCS_PROD"

        # Asserted for every contract. Code-first gates with
        # [HardenedOpenApiUi(Environments = ...)]; spec-first with UiEnvironments metadata on the
        # contract item. Both reach the same module, so both are held to the same answer.
        if [ "$DOCS_DEV" != "200" ]; then
            echo "   FAILED: the reference page should be served in development"
            FAILED=1
        fi

        if [ "$DOCS_PROD" = "200" ]; then
            echo "   FAILED: the reference page reached production"
            FAILED=1
        fi
    else
        echo "   FAILED: status=$CODE body='$BODY'"
        tail -20 "$OUT/run.log" || true
        FAILED=1
    fi
done

say "hardened-library"
# Framework packages only, so this one is verified against the build under test like hardened-web.
LIB="$WORK/library"
dotnet new hardened-library -n Sample -o "$LIB" --skip-restore
dotnet nuget add source "$FEED" --name template-verify-local --configfile "$LIB/nuget.config" >/dev/null
if ( cd "$LIB" && dotnet build -v q --nologo && dotnet test --no-build -v q --nologo ); then
    echo "   builds and tests"
else
    echo "   FAILED: hardened-library"
    FAILED=1
fi

# A dotted project name is the .NET norm and it is not merely cosmetic here: the module class is
# named after the project, and an unfiltered substitution produced "public partial class
# Acme.ApiLibrary" - which does not compile. Every template that names a class after the project
# needs the safe-identifier form, so the check runs against a name that has a dot in it.
say "dotted project names"
for TEMPLATE in hardened-library hardened-web; do
    DOTTED="$WORK/dotted-$TEMPLATE"
    dotnet new "$TEMPLATE" -n Acme.Sample -o "$DOTTED" --skip-restore
    dotnet nuget add source "$FEED" --name template-verify-local --configfile "$DOTTED/nuget.config" >/dev/null
    if ( cd "$DOTTED" && dotnet build -v q --nologo ); then
        echo "   $TEMPLATE builds as Acme.Sample"
    else
        echo "   FAILED: $TEMPLATE does not build with a dotted name"
        FAILED=1
    fi
done

# The Amz-dependent variants cannot be verified against the framework build under test, and that
# is a property of the dependency direction rather than an omission. Hardened.Amz depends on
# published Hardened packages, so a generated project pinned to this run's 0.0.0-verify framework
# also drags in whatever version Amz was built against - NU1605, every time. They are therefore
# generated against the newest published version, which gates the templates themselves without
# claiming to gate the build under test.
say "AWS Lambda templates (published packages)"
PUBLISHED=$(curl -s "https://api.nuget.org/v3-flatcontainer/hardened.amz.function.lambda.runtime/index.json" \
    | tr ',' '\n' | tr -d '" ' | grep -E '^[0-9]' | tail -1)

if [ -z "$PUBLISHED" ]; then
    echo "   SKIPPED: could not reach nuget.org for the published Hardened.Amz version"
else
    echo "   pinned to $PUBLISHED, not this run's $VERSION"

    for AMZ in "hardened-function --trigger invoke" "hardened-function --trigger sqs" "hardened-web --host aws-lambda"; do
        set -- $AMZ
        AMZ_TEMPLATE="$1"; shift
        AMZ_OUT="$WORK/amz-$AMZ_TEMPLATE-$(echo "$*" | tr -cd 'a-z')"

        # No local feed added: these restore from nuget.org alone, on purpose.
        dotnet new "$AMZ_TEMPLATE" -n Sample -o "$AMZ_OUT" "$@" \
            --hardened-version "$PUBLISHED" --skip-restore

        if ( cd "$AMZ_OUT" && dotnet build -v q --nologo && dotnet test --no-build -v q --nologo ); then
            echo "   $AMZ_TEMPLATE $*: builds and tests"
        else
            echo "   FAILED: $AMZ_TEMPLATE $*"
            FAILED=1
        fi
    done
fi

say "host independence"
# The framework's claim is that handlers, filters, binding and routing do not change with the
# host. If that is true, only the host project may differ between two generated applications.
# Comparable only across hosts at the same contract, which is the claim being checked.
A="$WORK/kestrel-code"; B="$WORK/aspnet-code"
if [ -d "$A" ] && [ -d "$B" ]; then
    # Source only. bin/ and obj/ carry absolute paths and compiler output, which differ for
    # reasons that have nothing to do with the host.
    for PART in src/Sample tests/Sample.Tests; do
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

say "$([ $FAILED -eq 0 ] && echo 'templates verified' || echo 'TEMPLATE VERIFICATION FAILED')"
exit $FAILED
