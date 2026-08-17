# arxtis.dev portfolio website

- Separate repo at `C:\Projects-Repositories\arxtis.dev` (GitHub `NexerG/arxtis.dev`, **private**). Astro + Three.js.
- Deployed as a **Cloudflare Worker** (Workers Builds), NOT classic Pages. Live at `https://engine.arxtis.dev` and `https://arxtis-dev.nextnexerg.workers.dev`. Apex `arxtis.dev` did not resolve as of 2026-08-10.
- `wrangler.jsonc` + the `@astrojs/cloudflare` adapter are required to deploy; they arrived on a Cloudflare-generated `cloudflare/workers-autoconfig` branch and were **merged into `main` on 2026-08-10**. Any build branch lacking them fails, because `wrangler.jsonc` points `main` at `dist/_worker.js/index.js`.

## This repo's docs are published by that site
- The site's build downloads the Project-Aurora `main` tarball and renders `DOCUMENTATION/Engine` at `/docs`.
- It is a **build-time snapshot**. Editing the vault changes the public site only when a rebuild runs.
- Rebuild triggers: a push to the website repo's `main`, or the daily `scheduled-rebuild.yml` cron POSTing a Cloudflare deploy hook (`CF_DEPLOY_HOOK_URL` GitHub secret).
- **2026-08-10 incident**: site had not rebuilt since the 2026-06-12 launch build. The 4 docs added after launch (SETTINGS, Settings Registry, Document Layout Engine, Rich Text Document) all 404'd. Cause: the `CF_DEPLOY_HOOK_URL` secret was never created, so the cron's `curl -fsS -X POST ""` failed every morning and emailed a failure notice.

## Consequences for vault edits here
- Renaming a doc file changes its public URL (slug = slugified path) and breaks inbound wikilinks until rebuilt.
- `Status: WIP` frontmatter, or membership in the `EXCLUDE` set in the site's `scripts/fetch-docs.mjs` (currently `Components/WIP.md`), keeps a doc OFF the public site.
- `%% comments %%` are stripped before publish; `![[*.base]]` / `![[*.excalidraw]]` embeds are dropped; mermaid renders client-side.
- Frontmatter `Status` / `Dependencies` / `Namespace` / `SourceFiles` render as a public metadata panel.

## Other site content
- `/projects` = curated `manifests/projects.xml` + live GitHub API (fallback: committed `src/data/github-cache.json`, refresh via `npm run refresh-cache`). Needs a `GITHUB_TOKEN` build var or shared CI IPs hit the 60/hr limit.
- `/videos` = YouTube IDs in `manifests/videos.xml`.
