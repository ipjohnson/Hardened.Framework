---
name: release
description: Cut a release of this repository. Runs the preflight checks that have actually failed before, bumps the version line, dry-runs the pack at the real version, tags, and watches it to nuget.org. Use when asked to release, cut, tag or ship a version.
---

# Release

Invoking this **is** the authorisation. Do not ask whether to proceed, whether to bump the line, or
whether to tag. Ask only when a preflight check below fails, because each of those has shipped a bad
release before.

Take the version from the invocation: `/release 0.31.0-rc1000`. If none was given, ask for it and
stop.

## Which repository

`git remote get-url origin` decides. `Hardened.Framework` and `DependencyModules` release
differently and the difference is in §4.

## 1. Preflight

Stop and report if any of these fail. Otherwise say what you found in one line each and continue.

**On main, synced, clean.** `git fetch origin && git status -sb`. A release is a snapshot of main.
An unpushed local commit gets absorbed into somebody's squash merge; a dirty tree gets tagged.

**The version is not already a tag.** `git tag -l "v$VERSION"`. nuget.org can unlist a version but
never remove it.

**Open pull requests are not in this release.** `gh pr list`. Not a blocker. Name them, so nobody
finds out afterwards that their work missed the cut.

**Everything the release workflow itself checks, before the tag rather than after it.** This is the
rule the rest of the section is only an instance of: `release.yaml` runs its guards *after* it has
already pushed to nuget.org, so a guard that fails leaves a published release and a red run. Read its
step list and run the same checks here.

```bash
grep -n 'name: ' .github/workflows/release.yaml | sed -n '/Resolve version/,$p'
```

Two of them have failed a real release from this repository, and both are cheap to run locally.

*The pack list covers every packable project.* The workflow has a step for this now, and its
`EXPECTED` count is a separate, weaker guard: `EXPECTED` compares what it packed against a number, so
it catches a package that was *removed* and is blind to one that was *added*. Run the step's own
logic, or this equivalent:

```bash
sed -n '/name: Pack/,/EXPECTED=/p' .github/workflows/release.yaml \
  | grep -oE 'src/[^ ]+\.csproj' | sort -u > /tmp/packlist.txt

find src -name '*.csproj' -not -path '*/obj/*' \
  | grep -viE '\.Tests?/|\.SUT/|IntegrationTests/|Benchmarks/|PublicApi/' | sort -u \
  | while read -r p; do grep -qiE '<IsPackable>\s*false\s*</IsPackable>' "$p" || echo "$p"; done \
  | sort -u > /tmp/packable.txt

comm -13 /tmp/packlist.txt /tmp/packable.txt   # packable, unlisted: templates only
comm -23 /tmp/packlist.txt /tmp/packable.txt   # listed, not packable: must be empty
```

Case-insensitive on `IsPackable`, because at least one project writes `False`. The six projects under
`src/Templates/**/templates/` are scaffolding content rather than packages and are the only allowed
entries in the first list.

*The documented version is the one being released.* Every copyable `PackageReference`,
`<HardenedVersion>` and `dotnet new install` line under `docs/` outside `design/` must cite the new
version. This failed v0.31.0-rc1000 **after** the packages had shipped, which is the whole reason
this section is written the way it is.

```bash
VERSION=<the version>
COPYABLE='(Version="|<HardenedVersion>|Hardened\.Templates@)[0-9]+\.[0-9]+\.[0-9]+-rc[0-9]+'
grep -rnE "$COPYABLE" docs --include='*.md' --exclude-dir=design | grep -v 'Hardened\.Amz\.' \
  | grep -oE "$COPYABLE" | grep -oE '[0-9]+\.[0-9]+\.[0-9]+-rc[0-9]+' | sort -u
```

Sweep the whole of `docs/` outside `design/`, including the prose line in `reference/packages.md`
that names the current line. Leave `design/` alone: those are maintainer notes and cite historical
versions on purpose, and leave any line mentioning `Hardened.Amz.`, which is frozen.

## 2. Bump the open line

**Framework only, and it ships with the release rather than after it.**

`RELEASE_LINE` in `.github/workflows/build-package.yaml` names the line previews are stamped under,
which is the line **currently open**, not the last one released. Release 0.31.0-rc1000 while it
still says 0.31.0 and every subsequent preview stamps `0.31.0-previewNNNNNN`, which sorts *below*
the release, and the feed can serve a preview over it. That is not hypothetical: it sat at 0.23.0
through the whole of 0.30.0-rc1000.

Move it to the next line and update the comment above it to say what has now released. Default to
the next minor and state the choice in the PR body, because the sequence skips numbers on purpose
(there was no 0.7.0; 0.23.0 through 0.29.0 were skipped).

Commit it with the documentation sweep and anything else the release needs, open **one** pull
request, wait for CI, merge. Do not put the version in the branch name.

## 3. Dry run, before the tag

The tag publishes. Everything below is cheap and has caught real problems.

```bash
export NUGET_PACKAGES=/private/tmp/release-dry/cache   # never the real cache
export PATH="$HOME/.smithy-cli/bin:$PATH"              # the pinned Smithy CLI, or rows skip silently

dotnet build Hardened.slnx --configuration Release -p:ContinuousIntegrationBuild=true > /tmp/rel.log 2>&1
echo "exit: $?"
```

**Read the exit code, never the tail.** A restore that resolves an assembly two ways prints MSB3277
by the hundred and fills any `head -N` window above the real error.

Then pack every project in the list at the real version into a throwaway folder feed and confirm the
count matches `EXPECTED`. Redirect `NUGET_PACKAGES` so the global cache is never poisoned with a
version that is about to exist for real.

Run `scripts/verify-templates.sh` when the release touches the test harness, the templates or
anything a scaffolded project consumes. It is the only thing here that exercises the framework the
way a new user meets it, and it has caught two defects that a green solution suite did not.

**After any local `verify-templates.sh` run, purge the scratch packages:**

```bash
find ~/.nuget/packages /private/tmp -maxdepth 3 -type d -name '99.0.0-verify*' -exec rm -rf {} +
```

With a sibling `DependencyModules` checkout present, the packed nuspecs pin it at the verify version,
and the leftovers poison later restores in a way a fresh clone and a fresh cache do not clear.

## 4. Tag

```bash
git tag -a "v$VERSION" -m "<one line, then what the release contains>"
git push origin "v$VERSION"
```

The tag is the source of truth for the published version; nothing in the tree names it.

**DependencyModules differs.** Its version lives in `Directory.Build.props` as `VersionPrefix` and
`FileVersion`, and it keeps a `CHANGELOG.md`. Bump both and add the entry in the release pull
request, then tag. It has no `RELEASE_LINE` and no pack-list drift problem: its workflow lists nine
projects and its previews are `$(VersionPrefix)-ci.$(run_number)`, so the prefix is what has to name
the version being worked toward.

## 5. Watch it land

Monitor the release workflow to completion, then confirm **every** package indexed, not one.
Indexing is per package and partial for a while: this line has published with two of nine visible
for several minutes while nuget.org validated the rest, and a consumer restoring in that window gets
a version-not-found on whichever package is still pending.

```bash
for p in <every package id>; do
  printf '%-40s ' "$p"
  curl -s "https://api.nuget.org/v3-flatcontainer/${p,,}/index.json" | jq -r '.versions[-1]'
done
```

`grep -c` exits non-zero when it finds nothing, so a polling loop written as `$(... | grep -c ...)
|| echo 0` yields `"0\n0"` and the arithmetic fails. Compare a fetched value instead of counting.

Report the tag, the run, the package count, and anything that was skipped.

## What this does not do

It does not decide whether an open pull request should have made the cut, and it does not force a
release over a failed preflight. Both are the maintainer's call, and both are the only two things
worth interrupting them for.
