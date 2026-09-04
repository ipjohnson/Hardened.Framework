#!/usr/bin/env bash
#
# Verify the templates: build the framework, pack it and them, install the packed template,
# generate a project from every supported combination of all three templates, and prove each one
# restores, builds, tests and - where it serves HTTP - answers. This is the gate a release runs.
#
# The packed nupkg is installed rather than the template folder, deliberately. A template tested
# from source proves nothing about packaging, which is where the 0.8.0-rc1000 quickstart broke.
#
# Usage: scripts/verify-templates.sh [host:contract[:model[:client]] ...]
#        default: the template default (response) on three host/contract rows, throws and union
#        on both spec directions, kestrel:smithy rows when the pinned CLI is present, and one
#        row with --client none to prove the opt-out
#
# smithy is skipped unless the Smithy CLI is on PATH at the pinned version - a build without it
# fails by design (HSMT011), and that is the toolchain's problem rather than the template's.
#
# The Kiota client is not skipped when Kiota is absent: the generated project restores the tool
# from NuGet inside its own build, so a machine that can restore packages can generate the
# client, and the client rows are the default rows.
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
    # The client is the default, so every row above exercises it; the last row is the opt-out,
    # spelled with the model it keeps so the fourth field reads as the fourth option.
    COMBOS=(kestrel:code aspnet:code kestrel:openapi
            kestrel:code:throws kestrel:openapi:throws
            kestrel:code:union kestrel:openapi:union
            kestrel:code:response:none)

    if command -v smithy >/dev/null 2>&1 && [ "$(smithy --version 2>/dev/null)" = "$SMITHY_PIN" ]; then
        # The throws model too, not only the default. Smithy's half of the property had never run:
        # the targets file did not pass $(HardenedResponseModel) at all, so a Smithy project asking
        # for a response set got throws mode and got it silently. A row that only ever exercises
        # the default is how that survived.
        COMBOS+=(kestrel:smithy kestrel:smithy:throws)
    else
        FOUND="$(command -v smithy >/dev/null 2>&1 && smithy --version || echo none)"
        echo "note: skipping the smithy contract - it needs the Smithy CLI at $SMITHY_PIN, found $FOUND"
    fi
fi

say() { printf '\n\033[1m== %s\033[0m\n' "$*"; }

# The defect class the second trial shipped, checked at generation rather than left to a build
# that cannot see it. A file whose whole body sits behind a false condition reaches the output as
# zero bytes, and a conditional inside a table cell can desynchronise the markdown evaluator and
# eat the rest of the README. Both build clean, which is exactly why they get their own check.
check_generated() {
    local out="$1"
    local empty

    empty=$(find "$out" -type f -empty -not -path '*/bin/*' -not -path '*/obj/*' | head -5)

    if [ -n "$empty" ]; then
        echo "   FAILED: generation left zero-byte file(s) in $out:"
        echo "$empty" | sed 's/^/     /'
        FAILED=1
    fi

    # Every template's README ends with the same section and closing line, so a truncated one is
    # detectable without knowing which options were on.
    if [ -f "$out/README.md" ]; then
        if ! grep -q 'Where to go next' "$out/README.md" || \
           [ "$(tail -1 "$out/README.md")" != "  code rather than reading it" ]; then
            echo "   FAILED: $out/README.md does not end where the template's does"
            FAILED=1
        fi
    fi
}

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

# The two Kiota pins the template carries have to agree before anything is scaffolded: the tool
# in .config/dotnet-tools.json generates the client, the bundle in Directory.Packages.props is
# what the generated code compiles against, and `kiota info` says which bundle a tool expects.
# The client project checks the same thing at build (HTPL003); checking here fails a release before
# a user's first build does.
say "checking the Kiota pins"
TEMPLATE="$REPO/src/Templates/Hardened.Templates/templates/hardened-web"
KIOTA_TOOL=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["tools"]["microsoft.openapi.kiota"]["version"])' "$TEMPLATE/.config/dotnet-tools.json")
KIOTA_BUNDLE=$(sed -n 's/.*<KiotaBundleVersion>\(.*\)<\/KiotaBundleVersion>.*/\1/p' "$TEMPLATE/Directory.Packages.props")
KIOTA_PROBE="$WORK/kiota-pins"
mkdir -p "$KIOTA_PROBE/.config"
cp "$TEMPLATE/.config/dotnet-tools.json" "$KIOTA_PROBE/.config/"
( cd "$KIOTA_PROBE" && dotnet tool restore >/dev/null )
KIOTA_EXPECTS=$( cd "$KIOTA_PROBE" && KIOTA_OFFLINE_ENABLED=true KIOTA_TUTORIAL_ENABLED=false dotnet kiota info --language CSharp --json \
    | python3 -c 'import json,sys; t=sys.stdin.read(); d=json.loads(t[t.index("{"):]); print(next(x["version"] for x in d["dependencies"] if x["name"]=="Microsoft.Kiota.Bundle"))' )
if [ "$KIOTA_EXPECTS" != "$KIOTA_BUNDLE" ]; then
    echo "   FAILED: kiota $KIOTA_TOOL (.config/dotnet-tools.json) expects Microsoft.Kiota.Bundle $KIOTA_EXPECTS, and KiotaBundleVersion (Directory.Packages.props) is $KIOTA_BUNDLE"
    exit 1
fi
echo "   kiota $KIOTA_TOOL and Microsoft.Kiota.Bundle $KIOTA_BUNDLE agree"

# The repository carries the same pair once more, for the client generated over the Web
# integration application, and a release moves all four together: a template pinned to one Kiota
# and an integration suite proving another is two claims about what a Hardened document generates.
REPO_TOOL=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["tools"]["microsoft.openapi.kiota"]["version"])' "$REPO/.config/dotnet-tools.json")
REPO_BUNDLE=$(sed -n 's/.*<KiotaBundleVersion>\(.*\)<\/KiotaBundleVersion>.*/\1/p' "$REPO/src/IntegrationTests/Web/Hardened.IntegrationTests.WebApp.SUT.Client/Hardened.IntegrationTests.WebApp.SUT.Client.csproj")
if [ "$REPO_TOOL" != "$KIOTA_TOOL" ] || [ "$REPO_BUNDLE" != "$KIOTA_BUNDLE" ]; then
    echo "   FAILED: the template pins kiota $KIOTA_TOOL / bundle $KIOTA_BUNDLE; the repository's .config/dotnet-tools.json and Hardened.IntegrationTests.WebApp.SUT.Client.csproj pin $REPO_TOOL / $REPO_BUNDLE"
    exit 1
fi
echo "   the integration client pins the same pair"

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

    # host:contract, host:contract:model, or host:contract:model:client. A bare combo scaffolds
    # with no --response-model and no --client at all, for the same reason --HardenedVersion is
    # not passed below: the default is what a real user gets, so the default is what needs
    # testing - and since 0.19.0 the default model is response, since 0.20.0 the default client
    # is kiota. Naming either exercises the flag.
    CLIENT=default
    if [ "$REST" = "$CONTRACT" ]; then
        MODEL=default
    else
        MODEL="${REST#*:}"
        if [ "$MODEL" != "${MODEL%%:*}" ]; then
            CLIENT="${MODEL#*:}"
            MODEL="${MODEL%%:*}"
        fi
    fi

    say "host: $HOST   contract: $CONTRACT   response model: $MODEL   client: $CLIENT"
    OUT="$WORK/$HOST-$CONTRACT-$MODEL"
    [ "$CLIENT" != "default" ] && OUT="$OUT-$CLIENT"

    # --HardenedVersion is deliberately NOT passed. The template stamps the version it was
    # packed with as the default, and that default is what a real user gets - so it is what
    # needs testing. Passing it here would test the flag and leave the default unexercised.
    ARGS=(--host "$HOST" --contract "$CONTRACT" --skip-restore)
    [ "$MODEL" != "default" ] && ARGS+=(--response-model "$MODEL")
    [ "$CLIENT" != "default" ] && ARGS+=(--client "$CLIENT")

    dotnet new hardened-web -n Sample -o "$OUT" "${ARGS[@]}"

    check_generated "$OUT"

    # The generated nuget.config names nuget.org only, which is what a real user wants. The
    # verification run also needs the framework build that has not been published yet.
    dotnet nuget add source "$FEED" --name template-verify-local --configfile "$OUT/nuget.config" >/dev/null

    ( cd "$OUT" && dotnet build -v q --nologo )
    ( cd "$OUT" && dotnet test --no-build -v q --nologo )

    # The client rows: the document the library's build wrote is what the client generated from,
    # and it is the file a consumer commits - so it has to exist, and it has to be the served
    # document. The opt-out row: no property, no file, no client project.
    if [ "$CLIENT" = "none" ]; then
        if [ -d "$OUT/src/Sample.Client" ] || [ -e "$OUT/src/Sample/openapi" ] || [ -d "$OUT/.config" ] || \
           [ -e "$OUT/tests/Sample.Tests/TestClients.cs" ] || [ -e "$OUT/tests/Sample.Tests/SampleClientTests.cs" ]; then
            echo "   FAILED: --client none left client files behind"
            FAILED=1
        fi
        if grep -q "HardenedOpenApiOutput\|Kiota\|Sample.Client" "$OUT/src/Sample/Sample.csproj" "$OUT/Sample.sln" "$OUT/Directory.Packages.props" "$OUT/tests/Sample.Tests/Sample.Tests.csproj"; then
            echo "   FAILED: --client none left the client in a project or solution file"
            FAILED=1
        fi
    else
        if [ ! -s "$OUT/src/Sample/openapi/Sample.json" ]; then
            echo "   FAILED: the build did not write src/Sample/openapi/Sample.json"
            FAILED=1
        fi
        if [ -z "$(find "$OUT/src/Sample.Client/obj" -name 'kiota-lock.json' 2>/dev/null)" ]; then
            echo "   FAILED: Kiota did not generate the client"
            FAILED=1
        fi
        if [ -n "$(find "$OUT/src/Sample.Client" -maxdepth 1 -name '*.cs')" ]; then
            echo "   FAILED: generated code landed beside the client csproj rather than under obj/"
            FAILED=1
        fi
    fi

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

        # How many operations the served document actually describes.
        #
        # /docs answering 200 was the only thing asserted here, and an empty document renders as a
        # reference page with zero operations - which is indistinguishable from a working page by
        # every check above. That is exactly how the template shipped with
        # [Enable<OpenApiDocumentPublishing>] on the host module, where the generator sees no routes
        # and writes "paths": {}.
        #
        # Counted rather than grepped. The strings "/todos" and "paths" appear in a document that
        # describes nothing - in its own schema names, or in a Smithy AST's @http uri values - so a
        # text match credits a document for operations it does not have.
        #
        # --compressed because the document is stored and served gzipped; without it this parses the
        # gzip magic number.
        #
        # Code-first only. A spec-first document is the contract file itself, served verbatim, and
        # cannot be empty without the input being empty.
        DOC_OPS=-1
        if [ "$CONTRACT" = "code" ]; then
            DOC_OPS=$(curl -s --compressed --max-time 5 "http://localhost:$PORT/openapi.json" \
                | python3 -c 'import json,sys; print(len(json.load(sys.stdin).get("paths") or {}))' \
                2>/dev/null || echo 0)
        fi

        # The declared error paths, over a real socket. A response model exercised only at 200 is
        # indistinguishable from having no declared set at all, which is the thing worth proving
        # here - and the sample's 404 and 409 are the two statuses every mode has to answer the
        # same way, whether it reaches them by returning a case or by throwing.
        # The list route, and asserted as an array rather than as 200. A Smithy @httpPayload list
        # generated an empty record and answered {} - a 200 with nothing in it, which every check
        # phrased as a status code passes.
        LISTED=$(curl -s --max-time 5 "http://localhost:$PORT/todos" || true)

        MISSING=$(status_of "http://localhost:$PORT/todos/9999")
        DUPLICATE=$(curl -s -o /dev/null -w '%{http_code}' --max-time 5 \
            -X POST -H 'Content-Type: application/json' \
            -d '{"title":"Add an endpoint"}' \
            "http://localhost:$PORT/todos" || true)

        # Which status a create answers with, and the three-way split is the point.
        #
        # Code-first throws mode has one success type per handler and nowhere to name a status beside
        # it, so it answers 200. Specification-first throws mode answers 201, because the contract names
        # the status and the generated dispatch carries it - what that mode cannot do is name more
        # than one. The declared models answer 201 from the case itself, either way.
        #
        # Asserting the difference is what proves the flag reached the generated code rather than
        # only the csproj.
        CREATED=$(curl -s -o /dev/null -w '%{http_code}' --max-time 5 \
            -X POST -H 'Content-Type: application/json' \
            -d '{"title":"Written by the verification run"}' \
            "http://localhost:$PORT/todos" || true)

        if [ "$MODEL" = "throws" ] && [ "$CONTRACT" = "code" ]; then
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

        case "$LISTED" in
            \[*\]) echo "   list: $LISTED" ;;
            *)
                echo "   FAILED: GET /todos should answer a JSON array, got '$LISTED'"
                FAILED=1
                ;;
        esac

        if [ "$CREATED" != "$EXPECT_CREATED" ]; then
            echo "   FAILED: $CONTRACT/$MODEL should create at $EXPECT_CREATED, got $CREATED"
            FAILED=1
        fi

        if [ "$DOC_OPS" != "-1" ]; then
            echo "   /openapi.json describes $DOC_OPS path(s)"

            if [ "$DOC_OPS" -lt 1 ] 2>/dev/null; then
                echo "   FAILED: the published document describes no operations"
                FAILED=1
            fi
        fi

        # Something on the console. A generated application that starts, serves and prints nothing
        # gives whoever just ran it no reason to believe it is working - and no logging provider is
        # registered by the framework, so this is the template's to get right.
        if ! grep -q . "$OUT/run.log" 2>/dev/null; then
            echo "   FAILED: the application produced no console output"
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
check_generated "$LIB"
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
    check_generated "$DOTTED"
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
    check_generated "$AMZ_OUT"
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
# The -default suffix, because that is what the bare rows' output directories are named now that
# they scaffold without the flag. The old suffixless paths matched nothing, so this check was
# silently skipped on every run - which is why it now says so instead of saying nothing.
A="$WORK/kestrel-code-default"; B="$WORK/aspnet-code-default"
if [ -d "$A" ] && [ -d "$B" ]; then
    # Source only. bin/ and obj/ carry absolute paths and compiler output, which differ for
    # reasons that have nothing to do with the host.
    # The client too: it is generated from the library's document and references nothing from
    # the host, so it has no reason to differ.
    for PART in src/Sample src/Sample.Client tests/Sample.Tests; do
        if diff -r -x bin -x obj "$A/$PART" "$B/$PART" >/dev/null 2>&1; then
            echo "   identical across hosts: $PART"
        else
            echo "   FAILED: $PART differs between kestrel and aspnet"
            diff -r -x bin -x obj "$A/$PART" "$B/$PART" | head -20
            FAILED=1
        fi
    done
else
    echo "   skipped: both hosts are not in this run's combinations"
fi

say "renamed value"
# --response-model standard was the throws mode's name until 0.19.0. The choice stays accepted for
# one release and scaffolds the same project, and this is the row that notices if either half
# stops being true - at generation, because the claim is about the template, not the build.
if [ -d "$WORK/kestrel-code-throws" ]; then
    OUT_ALIAS="$WORK/alias-standard"
    dotnet new hardened-web -n Sample -o "$OUT_ALIAS" --host kestrel --contract code \
        --response-model standard --skip-restore
    # openapi/ too: the throws row was built and its build wrote the document; this one was only
    # scaffolded, and the claim is about what the template writes.
    if diff -r -x bin -x obj -x nuget.config -x openapi \
        "$WORK/kestrel-code-throws/src" "$OUT_ALIAS/src" >/dev/null 2>&1; then
        echo "   --response-model standard scaffolds the throws project"
    else
        echo "   FAILED: --response-model standard no longer scaffolds what throws scaffolds"
        FAILED=1
    fi
else
    echo "   skipped: kestrel:code:throws is not in this run's combinations"
fi

dotnet new uninstall Hardened.Templates >/dev/null 2>&1 || true

say "$([ $FAILED -eq 0 ] && echo 'templates verified' || echo 'TEMPLATE VERIFICATION FAILED')"
exit $FAILED
