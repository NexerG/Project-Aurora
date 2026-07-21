# arxtis.dev portfolio website

- Separate repo at `C:\Projects-Repositories\arxtis.dev` (intended GitHub: `NexerG/arxtis.dev`), NOT part of this solution. Astro + Three.js, Cloudflare Pages, domain arxtis.dev.
- **This repo's `DOCUMENTATION/Engine` vault is published**: the site's build downloads the Project-Aurora `main` tarball and renders the vault at `arxtis.dev/docs`.
- Consequences for vault edits here:
  - Renaming a doc file changes its public URL (slug = slugified path) and breaks inbound wikilinks until rebuilt.
  - `Status: WIP` frontmatter (or listing in the `EXCLUDE` set in the site's `scripts/fetch-docs.mjs`; currently `Components/WIP.md`) keeps a doc OFF the site.
  - `%% comments %%` are stripped before publish; `![[*.base]]` / `![[*.excalidraw]]` embeds are dropped; mermaid renders.
  - Frontmatter `Status`/`Dependencies`/`Namespace`/`SourceFiles` render as a public metadata panel.
- Site refreshes GitHub data + docs on push to its repo and via daily cron (GitHub Action → CF deploy hook). Pushing to Project-Aurora alone does NOT redeploy the site until the next cron run.
- Project descriptions on `arxtis.dev/projects` come from GitHub repo descriptions (curated list in site's `manifests/projects.xml`); videos from `manifests/videos.xml` (YouTube IDs).
