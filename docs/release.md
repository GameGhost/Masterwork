# Release Process

Reusable runbook for cutting a Masterwork release: Windows build, signed Android build, and the
GitHub Releases on both this repo and the companion `Masterwork-Modules` repo. Follow top to bottom.

## 0. Version bump

In `src/Masterwork.App/Masterwork.App.csproj`:

```xml
<ApplicationDisplayVersion>0.1.1</ApplicationDisplayVersion>
<ApplicationVersion>1</ApplicationVersion>
```

- `ApplicationDisplayVersion` is the user-facing semver (`X.Y.Z`) — bump on every release.
- `ApplicationVersion` is Android's `versionCode` — a plain incrementing integer, independent of the
  display version. Bump it on every release that changes the Android build, even between releases
  that share the same `ApplicationDisplayVersion` — Android treats `versionCode` as the sole
  update/downgrade signal, so a repeat value means the Play Store / sideload install will refuse to
  treat the new APK as an update.

Commit the version bump with (or ahead of) whatever change is driving the release.

## 1. Pre-flight

```powershell
dotnet build src/Masterwork.slnx
dotnet test src/Masterwork.Tests/Masterwork.Tests.csproj
```

All tests must pass. Confirm `git status` is clean and everything intended for the release is
committed and pushed to `origin/main` — the GitHub release should point at a commit that's actually
on the remote.

## 2. Windows build (self-contained, unpackaged)

```powershell
# Make sure no debug instance of the app is running first — it locks the exe and the publish will
# silently produce a stale/incomplete output:
Get-Process Masterwork.App -ErrorAction SilentlyContinue

dotnet publish src/Masterwork.App/Masterwork.App.csproj `
  -f net10.0-windows10.0.19041.0 -c Release `
  -p:RuntimeIdentifierOverride=win-x64 -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true `
  -p:BuildAndroid=false

$src = "src\Masterwork.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish"
$dest = "Masterwork-<VERSION>-win-x64.zip"
if (Test-Path $dest) { Remove-Item $dest -Force }
Compress-Archive -Path "$src\*" -DestinationPath $dest
```

`PublishSingleFile=true` does **not** work for MAUI Windows apps — don't try to add it. The
`RuntimeIdentifierOverride`-conditional `PropertyGroup` already in `Masterwork.App.csproj` is a
required workaround for [WindowsAppSDK#3337](https://github.com/microsoft/WindowsAppSDK/issues/3337);
don't remove it.

## 3. Android build (signed APK)

**Run this step yourself, in a terminal Claude Code doesn't share** — the signing password
shouldn't be typed into an agent-visible shell. Claude Code can prep everything else and verify the
result afterward, but not run this command.


```powershell
$env:MwSigningPass = "<your password>"   # not committed anywhere, not shared with the agent

dotnet publish src/Masterwork.App/Masterwork.App.csproj `
  -f net10.0-android -c Release `
  -p:AndroidKeyStore=true `
  -p:AndroidPackageFormats=apk `
  -p:AndroidSigningKeyStore=masterwork-release.keystore `
  -p:AndroidSigningKeyAlias=masterwork `
  -p:AndroidSigningKeyPass=env:MwSigningPass `
  -p:AndroidSigningStorePass=env:MwSigningPass

Remove-Item env:\MwSigningPass
```

- **`-p:AndroidKeyStore=true` is required** — it defaults to `false`, and when it's missing every
  other `AndroidSigning*` property below is silently ignored (no error, no warning). The build still
  "succeeds" and still names its output `-Signed.apk`, but MAUI falls back to auto-signing with its
  own debug key instead — verify (below) always, don't trust the filename. Confirmed against
  [Microsoft's own publish-cli docs](https://learn.microsoft.com/en-us/dotnet/maui/android/deployment/publish-cli?view=net-maui-10.0),
  whose own example command includes it.
- `env:MwSigningPass` (no `$`) is an MSBuild-parsed prefix meaning "look up this OS environment
  variable at build time" — it is **not** PowerShell interpolation. Using `$env:MwSigningPass` here
  would leak the literal password onto the command line/process listing. The only place the `$`
  belongs is the `$env:MwSigningPass = "..."` assignment line above. This is a real, currently-documented
  `AndroidSigningKeyPass`/`AndroidSigningStorePass` feature (same source as above) — not generic
  MSBuild property syntax, so it won't work on unrelated `-p:` properties, only these two. Not
  supported when `AndroidPackageFormats` is `aab` (use `file:` instead there).
- Output APK: `src\Masterwork.App\bin\Release\net10.0-android\...\ca.digitalghost.masterwork.app-Signed.apk`.
- **If a rebuild produces a file with an unchanged timestamp**, the signing/packaging target didn't
  actually re-run (a stale incremental build bug seen during the v0.1.0 release — the output file
  looked signed but wasn't). Delete `src\Masterwork.App\bin\Release\net10.0-android` and
  `src\Masterwork.App\obj\Release\net10.0-android`, then re-run the publish.

Once built, **always** verify it's genuinely signed with your release key before handing it off —
both the missing-`AndroidKeyStore` and the stale-build failure modes above produce a file that looks
correct (right name, right rough size) but isn't:

```powershell
apksigner verify --print-certs "<path-to-apk>"
```

The certificate DN must be the one from your own keystore, not `CN=Android Debug, O=Android, C=US`
(that's the auto-generated debug key — a sure sign one of the two failure modes above happened).

Should show a v2/v3 signature with the expected certificate DN. Rename the output to
`Masterwork-<VERSION>-android.apk` for the release.

## 4. GitHub Release — Masterwork

Tag format: `v<VERSION>` (e.g. `v0.1.1`), matching `ApplicationDisplayVersion`.

```powershell
gh release create v<VERSION> `
  "Masterwork-<VERSION>-win-x64.zip" `
  "Masterwork-<VERSION>-android.apk" `
  --repo GameGhost/Masterwork `
  --title "v<VERSION>" `
  --notes "<release notes — see template below>"
```

Release notes template (see `v0.1.0`'s release for a full example):

```markdown
<One paragraph: what changed in this release and why, from a player's perspective.>

**Installing:**
- **Windows**: download `Masterwork-<VERSION>-win-x64.zip`, unzip anywhere, run `Masterwork.App.exe`.
  No separate .NET install needed (self-contained build).
- **Android**: download the `.apk`, enable "install from unknown sources" for your browser/file
  manager, then open the file to install.

Get a scenario to play from the [Masterwork-Modules releases](https://github.com/GameGhost/Masterwork-Modules/releases).
```

## 5. Companion release — Masterwork-Modules

Only needed if module content changed since the last module release. From `Masterwork-Modules`:

```powershell
# Re-bundle any module whose content changed (repeat per module):
dotnet run --project ..\Masterwork\src\Masterwork.ModulePacker -- cost-of-disease cost-of-disease.mwm

gh release create v<VERSION> `
  "cost-of-disease.mwm" `
  "fear-of-the-unknown.mwm" `
  "a-time-of-war.mwm" `
  "my-fathers-work-template.mwm" `
  --repo GameGhost/Masterwork-Modules `
  --title "v<VERSION>" `
  --notes "<what changed in the module content>"
```

All four bundles ship together even though `my-fathers-work-template` isn't a playable scenario
(it's a design/reference module) — since v0.2.0, every Modules release includes it alongside the
three scenarios rather than treating it as a separate, unbundled artifact.

The Modules repo's own release version doesn't need to track the app's version lockstep — only cut
one when module content actually changed. Each module bundle has its own `version` in its own
`manifest.yaml` (bumped via `scripts/repack.ps1 -Module <name> -IncrementVersion patch|minor|major`)
independent of the shared Modules-repo release tag — mention each bundle's own version in the
release notes (see the v0.2.0 release for the format) since they don't move in lockstep either.

## 6. Post-release checklist

- Verify the Windows zip actually launches on a clean machine (or at minimum, unzip-and-run
  locally) — don't rely on the publish succeeding as proof it works.
- Verify the Android APK's signature (step 3) before it's linked from the release.
- If the release fixes a bug reported by a specific user, follow up with them once the real
  (non-debug, non-test-build) release artifact is out.
- Check `README.md` and any other docs for hardcoded version numbers or stale "latest release"
  claims that should now point at the new version.
