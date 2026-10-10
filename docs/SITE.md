# Product and documentation site

The static site is built with PowerShell 7's bundled Markdown renderer, without
Node dependencies, external fonts, analytics or a JavaScript framework.

```powershell
.\scripts\Test-Site.ps1
python -m http.server 8080 --directory .\artifacts\site
# Open http://localhost:8080, stop the server with Ctrl+C when finished.
```

The landing page is `docs\site\index.html`; styles are `docs\site\style.css`.
Usage, contributor, architecture, release and security pages are rendered from
their **canonical repository Markdown**, not duplicated manuals. Local links
are rewritten to site pages or source on GitHub. All generated relative assets
and anchors are checked, including the `/window-gather/` project-site case.
Build output is ignored under `artifacts\site`.

`docs\images` holds real WinUI screenshots shared with the README. They were
captured using the approved isolated UI preview with simulated monitors/windows
and disposable settings. They are not fabricated app renders. The development
version in their title is not a claim about the latest public release.
No user's windows were gathered and no private recovery data was captured.
Recapture through the safe preview described in CONTRIBUTING, never production.

## Deployment readiness

The `Documentation site` workflow builds/checks PRs and main and uploads a Pages
artifact. It uses verified stable SHA-pinned Pages Actions. Only the deployment
job receives `pages:write` and `id-token:write`; no custom token is needed.
The workflow never enables Pages or changes repository settings.

At implementation time Pages is **not configured**. The owner must explicitly
choose **Settings → Pages → Build and deployment → Source: GitHub Actions** and
review any `github-pages` environment approvals. Then run the workflow or push
a docs update. Expected project URL: `https://martins-vds.github.io/window-gather/`;
this is a prospective URL, not a claim that the site is live.

Until configured, the workflow validates/uploads the site and explicitly skips
deployment. HTTP failures other than 404 fail instead of being disguised as
“not configured.” A 404 can mean absent Pages or API visibility limitations.
Do not bypass environment protections to make deployment pass.

## Licenses and support

The owner selected the [MIT License](../LICENSE) for the project, including its
documentation and site. Third-party dependencies retain their own licenses.
Private vulnerability reporting was
already enabled; no security setting was changed. CODEOWNERS alone does not
enforce review, and Dependabot configuration does not authorize automatic merges.
