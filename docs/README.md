# <picture><source media="(prefers-color-scheme: dark)" srcset="public/hardened-mark-dark.svg"><img src="public/hardened-mark.svg" alt="" width="34"></picture> Documentation

The user-facing site for [Hardened](https://ipjohnson.github.io/Hardened.Framework/), built with
VitePress from this directory:

```bash
cd docs
npm ci
npm run dev     # local preview
npm run build   # what CI publishes; fails on a dead internal link
```

`design/` is not part of the site. Those are maintainer notes — why an API behaves the way it does,
and what will catch you out — kept beside the code they describe and excluded from the build by
`srcExclude` in `.vitepress/config.ts`.

This lived in `ipjohnson-org/Hardened.Docs` until the move to one repository, which is why the
published URL changed from `/Hardened.Docs/` to `/Hardened.Framework/`.
