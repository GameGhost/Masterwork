# Release Process

Reusable runbook for cutting a Masterwork release: Windows build, signed Android build, and the
GitHub Releases on both this repo and the companion `Masterwork-Modules` repo. Follow top to bottom.

## 0. Version bump

The display version is `VersionPrefix` in the root `Directory.Build.props`; Android's build number is in
`src/Masterwork.App/Masterwork.App.csproj`:

```xml
<!-- Directory.Build.props -->
<VersionPrefix>0.1.1</VersionPrefix>
<!-- src/Masterwork.App/Masterwork.App.csproj -->
<ApplicationVersion>1</ApplicationVersion>
```

- `VersionPrefix` is the user-facing semver (`X.Y.Z`) — bump on every release. It is stamped on every
  assembly, shown in the app (main menu, help, web splash) and becomes the MAUI head's
  `ApplicationDisplayVersion`. Numbers only: a Windows package version can't carry a pre-release suffix.
- `ApplicationVersion` is Android's `versionCode` — a plain incrementing integer, independent of the
  display version. Bump it on every release that changes the Android build, even between releases
  that share the same display version — Android treats `versionCode` as the sole
  update/downgrade signal, so a repeat value means the Play Store / sideload install will refuse to
  treat the new APK as an update.

Commit the version bump with (or ahead of) whatever change is driving the release.

## 0.5. Signing identities

Three separate keys, with separate lifecycles. Don't conflate them:

| Key | Signs | Where | Rotation cost |
|---|---|---|---|
| `masterwork-release.keystore` | The side-load APK (step 2), and every APK Google Play delivers (it's the Play app signing key, step 7) | App repo root, gitignored | Android won't update an install signed by a different key — effectively unrotatable once published |
| `masterwork-upload.keystore` | The AAB uploaded to Play Console (step 2) | App repo root, gitignored | Low: Play Console can register a new upload key |
| `masterwork-signing.pfx` | Module/asset-pack bundles (step 4) and the catalog (step 5) | `Masterwork-Modules`, gitignored | New certificate + new pinned thumbprint + an app update |

The Microsoft Store package needs no key of ours: Partner Center signs what it distributes.

All are gitignored (`*.keystore`, `*.jks`, `*.pfx`, `*.p12`) in their respective repos and must never
be committed. All need a backup **outside** any working tree: losing the content-signing key doesn't
invalidate already-signed packages, but nothing new can ever be signed as the same publisher, which
means every existing install starts seeing an unrecognized-publisher prompt after the next release.

## 1. Pre-flight

```powershell
dotnet build src/Masterwork.slnx
dotnet test src/Masterwork.Tests/Masterwork.Tests.csproj
```

All tests must pass. Confirm `git status` is clean and everything intended for the release is
committed and pushed to `origin/main` — the GitHub release should point at a commit that's actually
on the remote.

## 2. Build the release artifacts

**Run this yourself, in a terminal Claude Code doesn't share** — it asks for the Android signing
passwords. Claude Code can prepare everything else and check the results afterwards.

```powershell
./scripts/release.ps1
```

It runs the tests, then builds every target into `artifacts\<VERSION>\` with a `SHA256SUMS.txt`:

| File | What it is | Goes to |
|---|---|---|
| `Masterwork-<VERSION>-win-x64.zip` | Portable Windows build, self-contained, unpackaged | GitHub release |
| `Masterwork-<VERSION>.msixupload` | Microsoft Store bundle (x64 + arm64), unsigned | Partner Center (step 6) |
| `Masterwork-<VERSION>.msixbundle` | The same bundle, unzipped from the upload | Local checks only |
| `Masterwork-<VERSION>-android.apk` | Side-load APK, signed with the release key | GitHub release |
| `Masterwork-<VERSION>-android.aab` | Play bundle, signed with the upload key | Play Console (step 7) |

`-Targets Windows,Store,Apk,Aab` picks a subset and `-SkipTests` skips the tests (for a rerun). Each
target is the same `dotnet publish` a human would type; the script exists so the release-specific
flags can't be forgotten:

- **Every build names its flavor** (`-p:WhiteLabelFlavor=MyFathersWork`), even though it's also the
  default, so a release never depends on what the default happens to be. See
  `src/WhiteLabel/WhiteLabel.props`.
- **Close any running `Masterwork.App` first** — it locks the files the Windows publish writes, and the
  publish would otherwise produce a stale output. The script refuses to start while it's running.
- **The portable build** needs the `RuntimeIdentifierOverride` workaround already in
  `Masterwork.App.csproj` for [WindowsAppSDK#3337](https://github.com/microsoft/WindowsAppSDK/issues/3337);
  don't remove it. `PublishSingleFile=true` doesn't work for MAUI Windows apps.
- **The Store build** is `-p:StorePackage=true`, once per architecture, bundled with `makeappx` from the
  `Microsoft.Windows.SDK.BuildTools` package NuGet already restored. It builds into `bin\Store\` and
  `obj\Store\` so its package identity never reaches the portable build. The identity comes from the
  flavor's `Store*` properties (step 6); the build stops with an error if they're empty.
- **Android signing**: the APK is signed with `masterwork-release.keystore` and the AAB with
  `masterwork-upload.keystore`. The script reads each password without echoing it, passes the APK's as
  an environment variable and the AAB's as a temporary file (an AAB can't take `env:`), and deletes
  both afterwards. It also clears the Android `bin`/`obj` Release folders first, because an incremental
  build once skipped re-signing and still produced a plausible-looking file.
- **Every Android artifact is checked** against the certificate in the keystore it was meant for. A
  build missing `AndroidKeyStore=true` silently signs with the debug key (`CN=Android Debug`) instead;
  the script fails rather than hand that over.
- **R8** shrinks and obfuscates the Java bytecode in Android Release builds (`Masterwork.App.csproj`),
  which Google Play expects of apps with more than 10 MB of it. A Release APK must be tested on
  a real device before publishing, because R8 can remove something that's only reached by reflection.

**After a flavor's `ApplicationId` changes, delete `src\Masterwork.App\obj` before building.** The
Windows package identity, and with it the local data folder, is generated into a cached
`Package.appxmanifest`. An incremental build doesn't regenerate it, because that step only reruns
when the manifest file itself changes.

### One-time setup: the Play upload key

The release key stays the app signing key, so side-loaded and Play installs can update each other.
Play Console needs a separate upload key, which authenticates uploads only and can be reset if lost:

Run this from the repository root. It takes the JDK path from `Directory.Build.local.props`, the same
file the build uses:

```powershell
[xml] $local = Get-Content Directory.Build.local.props
$keytool = Join-Path $local.Project.PropertyGroup.JavaSdkDirectory 'bin\keytool.exe'
& $keytool -genkeypair -v -keystore masterwork-upload.keystore -alias upload -keyalg RSA -keysize 2048 -validity 10000
```

keytool asks for a new password and then for the certificate's name fields. The name fields
aren't shown to players, so a name and country are enough. Use a different password from the
release key's.

This creates `masterwork-upload.keystore` in the repository root, beside `masterwork-release.keystore`.
That's where `release.ps1` looks for both. `*.keystore` is gitignored. Back up both keystores and
their passwords outside the working tree.

## 3. GitHub Release — Masterwork

Tag format: `v<VERSION>` (e.g. `v0.1.1`), matching `VersionPrefix`.

The release carries the two side-load artifacts and their checksums, never the Store or Play
packages. Draft the notes first and get them approved before running this.

```powershell
$a = "artifacts\<VERSION>"
gh release create v<VERSION> `
  "$a\Masterwork-<VERSION>-win-x64.zip" `
  "$a\Masterwork-<VERSION>-android.apk" `
  --repo GameGhost/Masterwork `
  --title "v<VERSION>" `
  --notes-file <approved notes file>
```

Upload a `SHA256SUMS.txt` holding only those two lines, rather than the script's full one, which also
lists the store packages.

Release notes template (see `v0.1.0`'s release for a full example):

```markdown
<One paragraph: what changed in this release and why, from a player's perspective.>

**Installing:**
- **Microsoft Store / Google Play**: <store links, once listed>.
- **Web**: <the flavor's web app address>, nothing to install.
- **Windows (portable)**: download `Masterwork-<VERSION>-win-x64.zip`, unzip anywhere, run
  `Masterwork.App.exe`. No separate .NET install needed (self-contained build).
- **Android (side-load)**: download the `.apk`, enable "install from unknown sources" for your
  browser/file manager, then open the file to install. It updates, and is updated by, the Google
  Play version: both are signed with the same key.

Scenarios download from inside the app (**New Game**).
```

## 4. Companion release — Masterwork-Modules

Only needed if module content changed since the last module release. From `Masterwork-Modules`:

```powershell
# Content signing — the release bundles must be signed, or players get an "unsigned content"
# prompt on every install. The .pfx is the maintainer's private key; never commit it.
$env:MASTERWORK_SIGNING_PASSWORD = "<pfx password>"

# Two runs: repack.ps1 defaults to -Mode module and packs ONLY module directories. The asset pack
# needs its own -Mode asset run — a bare repack.ps1 leaves mwf-common-assets.mwassets untouched.
.\scripts\repack.ps1 -SignWith <path>\masterwork-signing.pfx
.\scripts\repack.ps1 -Mode asset -SignWith <path>\masterwork-signing.pfx

Remove-Item env:\MASTERWORK_SIGNING_PASSWORD

# Verify every bundle before it goes out — each must report Valid with the expected thumbprint.
# Unsigned means a -SignWith run was missed; Invalid means the file changed after signing.
Get-ChildItem *.mwm, *.mwassets | ForEach-Object {
  dotnet run --project ..\Masterwork\src\Masterwork.ModulePacker -- verify $_.FullName
}

gh release create v<VERSION> `
  "cost-of-disease.mwm" `
  "fear-of-the-unknown.mwm" `
  "a-time-of-war.mwm" `
  "my-fathers-work-template.mwm" `
  "mwf-common-assets.mwassets" `
  --repo GameGhost/Masterwork-Modules `
  --title "v<VERSION>" `
  --notes "<what changed in the module content>"
```

The thumbprint every bundle reports must match the flavor's pinned `PublisherThumbprint` in the app
repo (`MyFathersWorkTheme.cs` in `src/Masterwork.App.Theme.MyFathersWork/`). A mismatch means the app
will treat this release as coming from an unrecognized publisher and prompt on every install — the
two are one decision, so they move together or not at all.

> **If the signature format itself changed, everything must be re-signed.** A signature is only
> readable by builds that understand the envelope that produced it; an older one reads as
> `Invalid`, not as "unsupported". `ModulePacker verify` on a bundle built by the previous release
> is the check — if it reports anything other than `Valid`, re-run the signing above before
> publishing, and re-publish the catalog too. The app discards a cached catalog that fails to
> verify and refetches, so players don't need to do anything, but nothing will install until the
> published artifacts are re-signed.

The catalog that makes these installable from inside the app is published separately, in step 5 —
after this step, never before.

All four module bundles ship together even though `my-fathers-work-template` isn't a playable
scenario (it's a design/reference module) — since v0.2.0, every Modules release includes it
alongside the three scenarios rather than treating it as a separate, unbundled artifact.
`mwf-common-assets.mwassets` ships alongside them too — all four modules declare it as a
`dependencies:` entry in their own `manifest.yaml`, and load without the app's `missing_dependency`
warning only once it's installed.

The Modules repo's own release version doesn't need to track the app's version lockstep — only cut
one when module content actually changed. Each module bundle has its own `version` in its own
`manifest.yaml` (bumped via `scripts/repack.ps1 -Module <name> -IncrementVersion patch|minor|major`)
independent of the shared Modules-repo release tag — mention each bundle's own version in the
release notes (see the v0.2.0 release for the format) since they don't move in lockstep either.

## 5. Publish the download catalog

This is what makes the release installable from inside the app. It runs **after** step 4 — entries
are named by release tag, so publishing the catalog first would point players at files that aren't
uploaded yet.

```powershell
# From Masterwork-Modules, using the bundles just uploaded. The tag argument must match step 4's
# release tag — it becomes the first path segment of every entry.
dotnet run --project ..\Masterwork\src\Masterwork.ModulePacker -- catalog `
  .catalog\catalog.json "My Father's Work — Official Content" v<VERSION> `
  cost-of-disease.mwm fear-of-the-unknown.mwm a-time-of-war.mwm `
  mwf-common-assets.mwassets

# Sign it — the app won't install from a catalog it can't verify.
dotnet run --project ..\Masterwork\src\Masterwork.ModulePacker -- signfile `
  .catalog\catalog.json <path>\masterwork-signing.pfx "<pfx password>"

# Must report Valid, with the same thumbprint the bundles carry.
dotnet run --project ..\Masterwork\src\Masterwork.ModulePacker -- verifyfile .catalog\catalog.json

git add .catalog/
git commit -m "catalog: v<VERSION>"
git push
```

Everything published here lives under `.catalog/` — `catalog.json`, its signature, and
`thumbnails/`. Grouping them keeps the repo root to module sources and build artifacts, and matches
the `.source/` convention already used for content that isn't itself a module.

Browse thumbnails are extracted from each module package (the image its manifest's `thumbnail.image`
names) and written to `.catalog/thumbnails/` — entries reference them by a path relative to the
catalog's own directory, so they must be committed along with it. Asset packs get none: they're never
browsed or installed directly, only pulled in as a module's dependency.

**Pushing the catalog publishes it.** Players never read it from GitHub: each flavor's site serves its
content from a copy deployed beside it (`https://myfatherswork.masterworkapp.com/api/content/...`).
The push runs Masterwork-Modules' `content-published` workflow, which sends a repository dispatch
that runs this repo's `Deploy web app` workflow. That stages the catalog, its signature, the
thumbnails and every package the catalog lists (downloaded from the releases and checked against the
catalog's hashes), and deploys. Allow a few minutes. The deploy fails, and the site keeps its previous
content, if a package is missing, doesn't match, or the total exceeds the Static Web Apps size limit;
check that run.

> **`.catalog/thumbnails/` is deliberately excluded from Git LFS** (see the modules repo's
> `.gitattributes`). The deploy reads them from a plain checkout, which holds an LFS *pointer file*
> rather than the object, so an LFS-tracked thumbnail would fail its hash check. Don't add these
> patterns back to LFS. Every other `*.png` in that repo stays on LFS as before.

Each entry also carries a `thumbnail_sha256`. The app caches thumbnails by that hash, so a catalog
refresh that doesn't change the art re-downloads nothing — which matters, since the thumbnails are
by far the largest thing a browse view fetches.

**The order of the package arguments is the order players see.** Entries are written in the order
the files are passed, and the app lists a source's modules in exactly that order — so arrange them
deliberately rather than alphabetically. Asset packs can go anywhere; they're never listed on their
own.

Each entry's hash and size are measured from the bundle bytes themselves, and its browse metadata
(title, description, languages, player count, playtime, dependencies) is read from that package's own
`manifest.yaml` — so nothing here is retyped by hand and nothing can drift from the content it
describes.

Two properties worth knowing:

- **Only republished entries need to move.** Entry paths start with the release tag, so a module that
  didn't change this cycle can keep pointing at the tag it was last published under. Passing every
  bundle (as above) is the simple case; a partial release means listing only what changed and
  carrying the unchanged entries forward.
- **Rolling back is reverting the catalog commit.** The releases themselves stay put, so a rollback
  never removes content anyone already installed — it just stops offering the newer version. The
  revert's push redeploys the site like any other catalog push.
- **To redeploy without a catalog change**, run `Deploy web app` by hand from this repo's Actions tab.

## 6. Microsoft Store (Partner Center)

### One-time setup

1. In Partner Center, create the app and **reserve its store name exactly as the flavor's
   `StoreDisplayName`** (`Masterwork: My Father's Work`). Partner Center rejects a package whose
   `Properties/DisplayName` isn't one of the app's reserved names.
2. From **Product management → Product identity**, copy `Package/Identity/Name`,
   `Package/Identity/Publisher` and `Package/Properties/PublisherDisplayName` into the flavor's
   `StoreIdentityName`, `StorePublisher` and `StorePublisherDisplayName`
   (`src/WhiteLabel/MyFathersWork.props`), and commit them. They're identifiers, not secrets.
3. The first submission fixes the listing to bundles, which is what the script produces. Keep it that
   way: a listing can't switch between a single package and a bundle afterwards.

### Each release

1. `artifacts\<VERSION>\Masterwork-<VERSION>.msixupload` from step 2. Its package version is
   `<VERSION>.0` (the Store reserves the fourth part), and each submission's must be higher than the
   last, so a resubmission of the same display version needs a version bump.
2. Optional local check: the Windows App Certification Kit (`appcert.exe`, from the full Windows SDK).
   Partner Center runs the same certification on upload.
3. Create a submission: upload the package, and fill in or confirm the listing from
   `docs/store-listing.md`, the age rating questionnaire, pricing (free), and the privacy policy
   (`https://masterworkapp.com/privacy.html`). Partner Center asks why the app declares
   `runFullTrust`: it's a desktop app (.NET MAUI on the Windows App SDK) that runs as an ordinary
   full-trust process.

A Store install keeps its data in its package folder, `%LOCALAPPDATA%\Packages\<package family
name>\LocalState\` (log under `logs\`), separate from the portable build's. The two never share
saves, settings or modules.

## 7. Google Play

### One-time setup

Google's identity and device verification must finish before anything can be published.

1. **Create the app** in Play Console, titled `Masterwork: My Father's Work` (the listing title is a
   Console field; the installed name stays `ApplicationTitle`).
2. **Make the release key Play's app signing key**, so Play installs and side-loaded APKs carry the
   same signature and can update each other. Play has two keys:

   | Play Console name | Our key | SHA-256 fingerprint starts |
   |---|---|---|
   | App signing key (Google signs what it delivers with it) | `masterwork-release.keystore` | `E3:72:CA:28` |
   | Upload key (signs the AAB you upload) | `masterwork-upload.keystore` | `0C:FF:88:69` |

   Open **Protected with Play › Play Store protection › Manage Play app signing** (named "App
   signing" in older Console layouts). The page always shows an app signing key certificate: if no
   key was chosen, Play generated one. Compare its SHA-256 with the table:

   - **It matches `E3:72:CA:28`**: already done.
   - **It doesn't**: Play generated its own key. That can be changed until a release reaches open
     testing or production; closed testing doesn't lock it. Choose **Change key**, then the option to
     use your own key from a Java keystore. Download the `pepk.jar` and
     `encryption_public_key.pem` the dialog offers, then run this in your own terminal from the
     repository root (it asks for the release keystore's password, then the key's password, which
     is usually the same):

     ```powershell
     [xml] $local = Get-Content Directory.Build.local.props
     $java = Join-Path $local.Project.PropertyGroup.JavaSdkDirectory 'bin\java.exe'
     $downloads = "$env:USERPROFILE\Downloads"
     & $java -jar "$downloads\pepk.jar" --keystore=masterwork-release.keystore --alias=masterwork --output=release-key-export.zip --include-cert --rsa-aes-encryption --encryption-key-path="$downloads\encryption_public_key.pem"
     ```

     Upload `release-key-export.zip` in the same dialog, then delete it: it's the release key,
     encrypted so only Google can read it. **Register the upload key's certificate in that same
     dialog** (exported below). If you don't, Play makes the release key the upload key too.

   Then check the **Upload key certificate** on the same page. It must start `0C:FF:88:69`. If it
   shows the release key's `E3:72:CA:28` instead, choose **Request upload key reset** there and
   attach the upload certificate. Until the reset takes effect, Play accepts only bundles signed with
   the release key, which the script can produce:
   `./scripts/release.ps1 -Targets Aab -SkipTests -UploadKeyStore masterwork-release.keystore -UploadKeyAlias masterwork`.

   To export the upload certificate (it asks for the upload keystore's password):

   ```powershell
   [xml] $local = Get-Content Directory.Build.local.props
   $keytool = Join-Path $local.Project.PropertyGroup.JavaSdkDirectory 'bin\keytool.exe'
   & $keytool -export -rfc -keystore masterwork-upload.keystore -alias upload -file upload_certificate.pem
   ```

   To read a fingerprint from a signed file without any password:
   `& $keytool -printcert -jarfile artifacts\<VERSION>\Masterwork-<VERSION>-android.apk` (or `.aab`).
3. **App content**: privacy policy URL, no ads, all functionality available without an account, the
   content rating questionnaire, target audience, and the Data safety form, all from
   `docs/store-listing.md`.
4. **Closed test, required for a new personal account before production**: at least 12 testers opted
   in for 14 continuous days. Create a closed testing track, add testers (an email list or a Google
   Group), upload the AAB, share the opt-in link, then apply for production access from the
   Dashboard once the 14 days are up. Google asks how testers were recruited and what feedback
   changed.

### Each release

1. `ApplicationVersion` (step 0) must be higher than any version code already uploaded, to any track.
2. Upload `artifacts\<VERSION>\Masterwork-<VERSION>-android.aab` to the track, with release notes
   ("What's new", 500 characters).
3. In **App bundle explorer**, check the DEX optimization, shrinking and obfuscation percentages
   (Google Play expects at least 25% of each for an app this size).

## 8. Post-release checklist

- Verify the Windows zip actually launches on a clean machine (or at minimum, unzip-and-run
  locally) — don't rely on the publish succeeding as proof it works.
- Verify the Android APK's signature (step 2) before it's linked from the release.
- Verify every content bundle's signature (step 4) — `ModulePacker verify` on each, expecting
  `Valid` and the pinned thumbprint. An unsigned or wrong-signer bundle isn't broken, but it makes
  every player acknowledge a prompt to install it.
- Verify the published catalog (step 5) — `ModulePacker verifyfile .catalog\catalog.json` must report `Valid`,
  the `Deploy web app` run it triggered must succeed, and
  `https://myfatherswork.masterworkapp.com/api/content/catalog.json` must serve the new catalog.
- If the release fixes a bug reported by a specific user, follow up with them once the real
  (non-debug, non-test-build) release artifact is out.
- Check `README.md` and any other docs for hardcoded version numbers or stale "latest release"
  claims that should now point at the new version.
