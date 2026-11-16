# Store listings — My Father's Work

What to enter in Google Play Console and Microsoft Partner Center for the My Father's Work flavor.
Text marked **Draft** needs the maintainer's approval before it's submitted, like release notes.
When the app's behaviour changes what a form answer should be, update this file in the same commit,
as with `site/privacy.html`.

## Identity

| Field | Value |
|---|---|
| Store title (both stores) | `Masterwork: My Father's Work` (Play allows 30 characters; this is 28) |
| Installed name | `My Father's Work` (`ApplicationTitle`; not set in either console) |
| Developer / publisher name | Masterwork |
| Category | Google Play: Games › Board. Microsoft Store: Games › Card & board |
| Price | Free, no in-app purchases, no ads |
| Website | `https://masterworkapp.com` |
| Support email | `support@masterworkapp.com` |
| Privacy policy | `https://masterworkapp.com/privacy.html` |
| Languages | App: English, French (Canada). Scenarios: English |

## Listing text — Draft

**Short description** (Play, 80 characters max):

> Free community companion app for the My Father's Work board game.

**Full description** (both stores):

> Masterwork: My Father's Work is a free, unofficial community companion app for *My Father's
> Work*, the board game from Renegade Game Studios. It plays the game's three official scenarios —
> The Cost of Disease, Fear of the Unknown and A Time of War — so your table can keep playing.
>
> - **Every official scenario**: download them inside the app, with their artwork, music and
>   narration.
> - **Rewind the story**: step back through what's happened, change a choice and carry on from
>   there.
> - **Your saves stay on your device**: no account, no ads, no tracking.
> - **Play anywhere**: the same app on Windows, Android and in your browser.
> - **Open to everyone**: scenarios use an open format, so anyone can fix, translate or write
>   them. The app is free and open source.
>
> You need a copy of the *My Father's Work* board game to play.
>
> This is an unofficial community project. It is not affiliated with or endorsed by Renegade Game
> Studios. *My Father's Work* and its artwork are Renegade Game Studios', used as made available in
> their community resources release.

**Release notes / What's new** (Play, 500 characters; per release, approved with the GitHub notes).

## Assets

| Asset | Google Play | Microsoft Store |
|---|---|---|
| Icon | 512 × 512 PNG — the theme's `logo.png` | Taken from the package |
| Feature graphic | 1024 × 500 PNG or JPEG, required | — |
| Screenshots | 2–8 phone screenshots (9:16 or 16:9, 1080 px wide or more); 7" and 10" tablet screenshots recommended | At least 1 desktop screenshot, 1920 × 1080 recommended |

Screenshots should show the main menu, New Game with the three scenarios, a story passage, and the
timeline (rewind). Take them from a release build with no debug overlays.

## Google Play: Data safety

| Question | Answer | Why |
|---|---|---|
| Does the app collect or share any of the required user data types? | **No** | Nothing leaves the device except requests for the catalog, thumbnails and scenario files. No analytics, crash reporting, accounts or ads. Saves, settings and the log stay on the device. |
| Account creation / deletion | Not applicable | There are no accounts. |

Android's own device backup (Auto Backup, excluding installed modules) goes to the player's Google
account, not to us, and isn't counted as collection. Check this table against `site/privacy.html`
before each submission; the two must agree.

## Google Play: other App content answers

| Section | Answer |
|---|---|
| Ads | No ads |
| App access | All functionality is available without special access |
| Target audience | **To confirm.** The board game is rated 14+. Choosing only ages 13 and over keeps the app out of the Families programme, which has extra requirements |
| Content rating (IARC) | **To confirm against the scenarios.** They contain text about disease, death, grave robbing and war, with illustrations from the game; no user interaction, no user-generated content, no purchases, no location sharing |
| News app, health, financial features, government app | No |

## Microsoft Store: other answers

| Section | Answer |
|---|---|
| Age rating | The same IARC questionnaire as Play; answer it consistently |
| Restricted capability `runFullTrust` | Desktop app built with .NET MAUI on the Windows App SDK; it runs as a standard full-trust desktop process |
| Device families | Windows desktop |

## Brand and content permission

Both stores check for impersonation and intellectual property. The listing uses the game's name and
its logo as the app icon. Keep ready, for a reviewer who asks:

- Renegade's community resources release, which permits using its files in projects:
  `https://renegadegamestudios.com/blog/my-fathers-work-app-update-community-resources/`
- The "unofficial, not affiliated" statement (above, in the app's About, and on the site).

Written permission from Renegade for the store listings would settle such a request fastest.
