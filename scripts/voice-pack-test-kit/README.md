# Voice pack test kit

Synthetic content for trying the voice pack workflows by hand before real voice packs exist. It is
for testing only: nothing here ships, and the certificate it makes is throwaway.

`build.ps1` writes, under `out\` (gitignored):

| Package | What it's for |
|---|---|
| Module `masterwork.voicekit` v1.0.0, v1.1.0 | Three passages in a loop, en-US and fr-CA text, one narration `audio_track` each. Its own en-US track `builtin` narrates all three |
| Pack `…voices.en-US` v1.0.0 | Track `builtin` (replaces the module's own) and a second track `female` |
| Pack `…voices.fr-CA` v1.0.0 | Track `lecteur`, missing the last passage (a gap) |
| Pack `…voices.fr-CA` v1.1.0 | The same track, complete. Listed only at stage 2 |
| Pack `…voices.cs-CZ` v1.0.0 | Needs module v1.1.0 |
| Pack `…voices.conflict` v1.0.0 | Adds en-US `female` again, which the en-US pack already adds |

Every narration file says aloud which pack, track, version and passage it is (Windows speech, or a
tone per file where that isn't available), so you can tell by ear what the app chose.

The catalog has two stages. Stage 1 lists module v1.0.0 and the first packs. Stage 2 adds module
v1.1.0 and fr-CA v1.1.0. Packages are built once and reused across stages; pass `-Rebuild` to
regenerate them.

## Setup

1. Trust the local development certificate once: `dotnet dev-certs https --trust`.
2. Build stage 1: `.\scripts\voice-pack-test-kit\build.ps1`
3. Serve it, and leave it running: `dotnet run scripts\voice-pack-test-kit\serve.cs`. It serves
   `https://localhost:5443/catalog.json` with CORS, so the web head can read it too.
4. Start the app: the Windows head, or the web head (`dotnet run --project src\Masterwork.App.Web`).
5. **Trust the kit's signer.** In Manage Modules, upload `out\site\kit\voicekit-1.0.0.mwm` and accept
   the unrecognized-signer prompt. Then delete the module again, so it can be installed from the
   catalog.
6. Add `https://localhost:5443/catalog.json` as a catalog source.

**Android** can't use this server: the emulator and devices don't trust the PC's development
certificate. Upload `out\site\` to any https static host instead, and add that URL as the source.

The app refreshes catalogs at start-up, and then at most daily. After switching stages, restart the
app (or reload the page on the web) to see the change.

## Walkthrough

Tick these off in order; each step's expected result is in italics.

**Installing with a module**
- [ ] In main-menu Options, set Voice Language to Français. Install the kit module from New Game.
  *Manage Modules lists the fr-CA voice pack as installed too.*
- [ ] Delete both, set Voice Language to Best match (with English as the app language), and install
  the module again. *No pack is installed: the module's own en-US track already narrates English.*

**New Game**
- [ ] Select the kit module. *The voice picker lists None, English and Français (no Čeština: that
  pack needs v1.1.0). It is preselected from the Options setting.*
- [ ] Play in English. *"Built-in English reader" plays, with no prompt.*
- [ ] With the fr-CA pack not installed, pick Français and press Play. *Download narration?, with the
  pack's size. Back returns to New Game. Play without narration starts silent. Download installs it,
  then starts with "French reader, pack fr-CA version 1.0.0".*
- [ ] Play on to the third passage in French. *No narration player there: fr-CA v1.0.0 has a gap.*

**In-game Options**
- [ ] *Voice Language lists None and the module's languages only, with no Best match or Same as
  module. It shows None when nothing is narrating.*
- [ ] Pick a language whose pack isn't installed and Apply. *Download or Cancel. Cancel puts the picker
  back and leaves the dialog open, with nothing applied. Download installs it and switches narration.*
- [ ] Change only the volume and Apply in a game that has no narration playing. *The game's stored
  voice language is kept: after Save and Quit and Continue, it asks about that pack again.*

**Override, tracks, refusals**
- [ ] Install the en-US pack from Manage Modules and play in English. *"English pack reader, replacing
  the built-in one". The track picker offers "Pack reader (replaces built-in)" and "Second reader";
  switching loads the other one without autoplay.*
- [ ] Install the conflict pack. *Refused: it adds a voice track the en-US pack already installed.*
- [ ] Look at Čeština in "Voice packs you can download". *"Needs Voice Pack Test Kit v1.1.0", and at
  stage 1 no update to install.*

**Stage 2: updates** (run `build.ps1 -Stage 2`, then restart the app)
- [ ] Manage Modules. *The module offers v1.1.0, fr-CA offers "Update to v1.1.0", and Čeština's
  refusal now offers the module update.*
- [ ] Play in French with fr-CA v1.0.0 installed. *Update narration? Not now plays v1.0.0. Update
  plays "version 1.1.0", which narrates the third passage too.*
- [ ] Update the module to v1.1.0, then install Čeština. *Installs and plays.*

**Saves**
- [ ] Play in French, Save and Quit, delete the fr-CA pack, then Continue. *Download / Play without
  narration / Back. After Play without narration, the next resume asks again.*
- [ ] Export that save and import it. *The import keeps its voice language.*

**Offline**
- [ ] Stop `serve.cs` and restart the app. *Play in a language with an update listed gives no update
  offer. A missing pack's Download fails with a message, and Play without narration still works.*

## Reset

Delete the kit module and its voice packs in Manage Modules, and remove the catalog source. The
trusted signer stays in the app's settings until the app's data is cleared.
