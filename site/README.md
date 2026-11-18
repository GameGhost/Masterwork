# masterworkapp.com

The project information site: plain HTML and CSS, no build step. Open `index.html` in a browser to
preview, or serve the folder with any static file server.

Deployed to its own Azure Static Web App by `.github/workflows/site.yml` whenever `site/` changes on
`main`. `staticwebapp.config.json` sets the 404 page and security headers. The Content Security
Policy allows only this site's own styles and images, so pages must not use inline `style`
attributes or scripts.

Keep `privacy.html` in step with what the app actually does: it's the page both app stores link to,
and its "Last updated" date changes with every edit to it.
