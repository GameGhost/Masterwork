# Asset provenance

This project packages a visual theme for the Masterwork app matching the look of the original
*My Father's Work* app, published by Renegade Game Studios.

The images under `AppIcon/` and `wwwroot/images/` are copied or modified from Renegade Game
Studios' own community-resources release for *My Father's Work*:

<https://renegadegamestudios.com/blog/my-fathers-work-app-update-community-resources/>

Per that release, individual files may be copied and modified as needed; the reference source
itself (the full asset/project archive it was drawn from) is not mirrored here — only the specific
files this theme actually uses, cherry-picked per screen.

This is a deliberate, documented exception to this repo's general rule against committing anything
from `Masterwork-Design\Reference\` (see the code repo's own `CLAUDE.md`) — that rule covers the
broader CC BY-NC-SA reference material used for content extraction, not this specific community
release.

Some files under `wwwroot/images/ui/` (`button_brown.png`, `button_green.png`, `button_red.png`,
`button_question.png`, `slider_base.png`, `slider_button.png`, `checkbox_outline.png`,
`checkbox_checkmark.png`, `input_small.png`, `button_back_orange.png`, `button_forward_orange.png`,
`arrow_left.png`, `arrow_right.png`),
`wwwroot/images/backgrounds/` (`border.png`, `panel_general.png`, `panel_general_small.png`),
`wwwroot/images/module-select/` (`hub-section-border.png`, `leather-background.png`), and
`wwwroot/images/help/` (`popup-parchment-border.png`) were copied from `my-fathers-work-template`'s
already-cropped/processed versions of the same underlying Renegade Game Studios source art
(`Masterwork-Modules` repo) rather than re-derived from `Reference/` directly — same provenance,
just via an already-processed copy.

`wwwroot/images/module-select/tile-border-inactive.png` and `tile-border-active.png` are a
different case: they were moved here from the `cost-of-disease` module's own `assets/images/`
(`Masterwork-Modules` repo), not from the RGS community release. Per that repo's own `CLAUDE.md`,
module assets are project-internal/original MWS content, not CC BY-NC-SA — these were
module-specific per-tile borders that became theme-owned defaults instead (any module can still
supply its own via `ModuleThumbnail.BorderInactive`/`BorderActive`, just not consumed by the app's
default rendering yet).

`wwwroot/fonts/germania-one-v21-latin-regular.woff2` and
`wwwroot/fonts/averia-libre-v16-latin-regular.woff2` are Google Fonts (SIL Open Font License) — an
unrelated, separately-licensed source, copied here (also via `my-fathers-work-template`, which
bundles the full family) purely for visual consistency between in-game chrome and the app's own
shell, not part of the RGS asset exception above.

CSS and any other original content in this project are Masterwork's own work.
