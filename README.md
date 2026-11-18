# Masterwork

A community companion app for the boardgame *My Father's Work* (Renegade Game Studios). Converts
the official scenario scripts into an open format (MWS) and plays them.

This is an independent fan project — **not affiliated with, endorsed by, or sponsored by Renegade
Game Studios.** *My Father's Work* and all associated names/artwork are the property of their
respective owners.

## Why this exists

Masterwork started as an attempt to keep *My Father's Work* playable and supportable — community
maintenance, translations, and new scenario content for the game.

That's still the immediate goal, but not the only one. The engine, MWS format, and player underneath
it were built to be content-agnostic — nothing about how a passage renders, how a timeline rewinds,
or how a popup transacts state is specific to this one game. The longer-term intent is for Masterwork
to also stand on its own as a general-purpose framework for branching narrative interactive
games/experiences — the `Extractor` is what's Cradle/*My Father's Work*-specific; the `Engine`,
format, and `App` are not.

## What's here

- **Engine** — a pure C# interpreter and game session: branching passages, popups, timeline
  rewind/step-back/step-forward with checkpoints, seeded RNG for reproducible replays.
- **Extractor** — converts the original Cradle script format into MWS (Masterwork Script), the
  open YAML-based format this project defines (see [`docs/mws-format-latest.md`](docs/mws-format-latest.md)).
- **App** — a Blazor Hybrid/WebAssembly player, themed to match the original app, running on
  Windows, Android, and the web.

Playable modules (converted scenarios) live in the separate
[Masterwork-Modules](https://github.com/GameGhost/Masterwork-Modules) repo.

## Getting the app

- **Web:** play *My Father's Work* at <https://myfatherswork.masterworkapp.com>, nothing to install.
- **Windows and Android:** download the latest build from [Releases](../../releases): a portable
  Windows zip and an Android APK for side-loading. Microsoft Store and Google Play listings are on
  the way.

The app lists the available scenarios and installs them itself. To install a scenario by hand
instead, download its `.mwm` file from
[Masterwork-Modules releases](https://github.com/GameGhost/Masterwork-Modules/releases) and add it
from the app's Start New Game screen.

More about the project, help, and the privacy policy: <https://masterworkapp.com>.

## Building from source

```powershell
dotnet build src/Masterwork.slnx
dotnet test src/Masterwork.Tests/Masterwork.Tests.csproj
```

See [`CLAUDE.md`](CLAUDE.md) for the full solution layout, architecture notes, and build details
(including Android's local JDK/SDK setup).

## License

[GNU AGPL-3.0](LICENSE). If you run a modified version of this code as a network service, the AGPL
requires you to make your modified source available to that service's users — see the license text
for the exact terms.

## Asset & content provenance

Some visual assets in `src/Masterwork.App.Theme.MyFathersWork/` are copied or modified from
Renegade Game Studios' own community-resources release for *My Father's Work* — see that project's
own [`NOTICE.md`](src/Masterwork.App.Theme.MyFathersWork/NOTICE.md) for the full citation and
terms. Everything else in this repo is original work.
