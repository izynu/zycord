# Connecting a custom domain

The site is published from this repository by GitHub Pages, from the workflow
in `.github/workflows/pages.yml`. Until a domain is connected it answers on
`https://izynu.github.io/zycord/`.

Connecting a domain is a launch gate. It cannot be completed from inside the
repository alone: it needs a domain you control and access to its DNS records.
This file records exactly what to do once you have both, so nothing has to be
worked out later.

## What is needed first

1. A domain you own. Any registrar is fine. A subdomain of an existing domain
   also works, for example `zycord.example.com`.
2. Access to that domain's DNS panel.
3. The repository published under a GitHub account or organisation, with Pages
   enabled and the `pages.yml` workflow having run at least once.

## Step 1: add the domain in GitHub

Open the repository, then **Settings**, then **Pages**, then **Custom domain**.
Enter the domain and save. GitHub writes a `CNAME` file at the root of the
published site and starts issuing a certificate.

Do not create `site/CNAME` by hand while the generator is in use. The workflow
uploads the `site` folder as-is, and a hand-written file there would be
overwritten on the next regeneration unless it is added to the generator.

## Step 2: point DNS at GitHub Pages

For a subdomain such as `zycord.example.com`, add one record:

| Type | Name | Value |
| --- | --- | --- |
| CNAME | `zycord` | `izynu.github.io` |

For the bare domain such as `example.com`, add four A records and four AAAA
records:

| Type | Name | Value |
| --- | --- | --- |
| A | `@` | `185.199.108.153` |
| A | `@` | `185.199.109.153` |
| A | `@` | `185.199.110.153` |
| A | `@` | `185.199.111.153` |
| AAAA | `@` | `2606:50c0:8000::153` |
| AAAA | `@` | `2606:50c0:8001::153` |
| AAAA | `@` | `2606:50c0:8002::153` |
| AAAA | `@` | `2606:50c0:8003::153` |

If a wildcard CNAME already exists on the domain, remove it, because it
conflicts with the certificate check.

## Step 3: wait, then enforce HTTPS

DNS usually settles within an hour and can take up to 24. GitHub issues the
certificate once the records resolve. When the certificate is ready, tick
**Enforce HTTPS** on the same Pages settings screen.

## Step 4: update the places that carry the address

These are the only files that name the site address. Change them together, then
regenerate and commit:

- `site/build_site.py`: the `SITE` constant used for canonical links and
  `og:url`.
- `app/renderer/app.js`: the About tab button that opens the site.
- `README.md`: the website link near the top.
- `site/assets/site.js`: no change needed.
- `docs/CUSTOM-DOMAIN.md`: this file, if the workflow is kept as a record.

After editing, run:

```
python site/build_site.py
python build.py
```

The Pages workflow regenerates the site and fails if the committed HTML does
not match the generator output, so the regenerated files must be committed in
the same change.

## Checking it worked

- `curl -I https://your-domain` returns `200` and a `server: GitHub.com`
  header.
- The address bar shows the padlock, and `http://` redirects to `https://`.
- `https://your-domain/sitemap.xml` and `https://your-domain/robots.txt` load,
  if the generator writes them.
- The Pages settings screen shows the domain with a green tick and
  "DNS check successful".

## What is already done

- The site has a favicon in both ICO and SVG form, and an Apple touch icon.
- The site has a privacy policy and terms page, linked from every page footer.
- The site carries no "Made with AI" tag or badge, and no generated-content
  marker of any kind.
- The canonical URL, Open Graph and Twitter card metadata are emitted by the
  generator, so changing one constant updates every page.
