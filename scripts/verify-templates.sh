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
#
# 99.0.0 rather than 0.0.0, and that is not cosmetic either. Hardened.Amz depends on published
# Hardened packages with a floor - ">= 0.10.0-rc1000" today - and a 0.0.0 version sits below it,
# so every Lambda project failed to restore with NU1605 rather than exercising anything. A real
# release is always above that floor, so the verification version has to be too, or the Amz
# templates can only ever be tested against the previous release instead of the build in hand.
VERSION="99.0.0-verify$(date +%s)"
FEED="${TMPDIR:-/tmp}/hardened-template-feed"
# Resolved with pwd -P, which is not cosmetic on macOS. $TMPDIR there is /var/folders/... and /var
# is a symlink to /private/var, so a launched process reports the resolved path in its command line
# while $OUT still holds the unresolved one - and `pkill -f "$OUT/src/Sample.Host"` below therefore
# matched nothing. Every generated application stayed running after its combination finished, and a
# later combination picking the same random port talked to an earlier one's server: a create that
# should answer 201 came back 409, because the todo already existed in a store from a previous run.
WORK="$(cd "${TMPDIR:-/tmp}" && pwd -P)/hardened-template-work"
SMITHY_PIN=1.73.0

COMBOS=("$@")
if [ ${#COMBOS[@]} -eq 0 ]; then
    # One combination per response model rather than the full cross product: the model is
    # orthogonal to the host, and proving that costs nothing beyond one of each.
    #
    # union is in the default list rather than opt-in. It needs the .NET 11 SDK, which this script
    # already requires for the framework itself, and the reason it is not conditional is that a row
    # nobody runs is a row nobody notices is broken: the first union generation failed on an XML
    # comment containing a double hyphen, which every other combination was immune to only because
    # that comment sat behind #if (unionMode).
    COMBOS=(kestrel:code aspnet:code kestrel:openapi
            kestrel:code:response kestrel:openapi:response
            kestrel:code:union kestrel:openapi:union)

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
    REST="${COMBO#*:}"
    CONTRACT="${REST%%:*}"

    # host:contract, or host:contract:model. Defaulted rather than required, so every combo written
    # before the response model existed still means what it said.
    if [ "$REST" = "$CONTRACT" ]; then
        MODEL=standard
    else
        MODEL="${REST#*:}"
    fi

    say "host: $HOST   contract: $CONTRACT   response model: $MODEL"
    OUT="$WORK/$HOST-$CONTRACT-$MODEL"

    # --HardenedVersion is deliberately NOT passed. The template stamps the version it was
    # packed with as the default, and that default is what a real user gets - so it is what
    # needs testing. Passing it here would test the flag and leave the default unexercised.
    dotnet new hardened-web -n Sample -o "$OUT" --host "$HOST" --contract "$CONTRACT" \
        --response-model "$MODEL" --skip-restore

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
                "http://localhost:$port/todos/1" || true)
            [ "$code" = "200" ] && return 0
            sleep 0.4
        done

        return 1
    }

    stop() {
        pkill -f "$OUT/src/Sample.Host" 2>/dev/null || true
        sleep 0.5
    }

    # Whatever happens after this point, the servers this combination started are not left running.
    # An abandoned one holds its port, and the next combination that lands on it is talking to the
    # previous application's state rather than its own.
    trap 'pkill -f "$WORK/.*/src/Sample.Host" 2>/dev/null || true' EXIT

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
        BODY=$(curl -s --max-time 2 "http://localhost:$PORT/todos/1" || true)
        DOCS_DEV=$(status_of "http://localhost:$PORT/docs")

        # The declared error paths, over a real socket. A response model exercised only at 200 is
        # indistinguishable from having no declared set at all, which is the thing worth proving
        # here - and the sample's 404 and 409 are the two statuses every mode has to answer the
        # same way, whether it reaches them by returning a case or by throwing.
        MISSING=$(status_of "http://localhost:$PORT/todos/9999")
        DUPLICATE=$(curl -s -o /dev/null -w '%{http_code}' --max-time 5 \
            -X POST -H 'Content-Type: application/json' \
            -d '{"title":"Add an endpoint"}' \
            "http://localhost:$PORT/todos" || true)

        # Which status a create answers with, and the three-way split is the point.
        #
        # Code-first Standard has one success type per handler and nowhere to name a status beside
        # it, so it answers 200. Specification-first Standard answers 201, because the contract names
        # the status and the generated dispatch carries it - what that mode cannot do is name more
        # than one. The declared models answer 201 from the case itself, either way.
        #
        # Asserting the difference is what proves the flag reached the generated code rather than
        # only the csproj.
        CREATED=$(curl -s -o /dev/null -w '%{http_code}' --max-time 5 \
            -X POST -H 'Content-Type: application/json' \
            -d '{"title":"Written by the verification run"}' \
            "http://localhost:$PORT/todos" || true)

        if [ "$MODEL" = "standard" ] && [ "$CONTRACT" = "code" ]; then
            EXPECT_CREATED=200
        else
            EXPECT_CREATED=201
        fi
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
        echo "   declared errors: /todos/9999=$MISSING  duplicate POST=$DUPLICATE"

        echo "   create: $CREATED (expected $EXPECT_CREATED for $MODEL)"

        if [ "$MISSING" != "404" ] || [ "$DUPLICATE" != "409" ]; then
            echo "   FAILED: expected 404 and 409, got $MISSING and $DUPLICATE"
            FAILED=1
        fi

        if [ "$CREATED" != "$EXPECT_CREATED" ]; then
            echo "   FAILED: $CONTRACT/$MODEL should create at $EXPECT_CREATED, got $CREATED"
            FAILED=1
        fi

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

# The Amz pin floats, which is what lets these be gated against the build under test at all.
# Hardened.Amz depends on published Hardened packages, so an exact pin here would name a version
# that does not exist yet for the whole window between the two repositories' releases. Floating it
# means the framework packages come from this run's feed while Hardened.Amz stays on its newest
# published release - which is precisely the state a release leaves the world in, so verifying it
# is verifying the thing that actually ships.
say "AWS Lambda templates"
for AMZ in "hardened-function --trigger invoke" "hardened-function --trigger sqs" "hardened-web --host aws-lambda"; do
    set -- $AMZ
    AMZ_TEMPLATE="$1"; shift
    AMZ_OUT="$WORK/amz-$AMZ_TEMPLATE-$(echo "$*" | tr -cd 'a-z')"

    dotnet new "$AMZ_TEMPLATE" -n Sample -o "$AMZ_OUT" "$@" --skip-restore
    dotnet nuget add source "$FEED" --name template-verify-local --configfile "$AMZ_OUT/nuget.config" >/dev/null

    if ( cd "$AMZ_OUT" && dotnet build -v q --nologo && dotnet test --no-build -v q --nologo ); then
        echo "   $AMZ_TEMPLATE $*: builds and tests"
        # Worth printing: a float that silently stopped resolving would otherwise look identical
        # to one that resolved to the right thing.
        grep -hoE '"Hardened\.Amz\.[A-Za-z.]+/[^"]+"' "$AMZ_OUT"/src/*/obj/project.assets.json 2>/dev/null \
            | tr -d '"' | sort -u | head -2 | sed 's/^/     resolved /'
    else
        echo "   FAILED: $AMZ_TEMPLATE $*"
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
