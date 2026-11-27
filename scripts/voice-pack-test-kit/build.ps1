<#
.SYNOPSIS
    Builds a synthetic module, voice packs and a signed test catalog for checking voice pack
    workflows by hand. Nothing here ships; see README.md beside this script.

.PARAMETER Stage
    1 (the default) lists module v1.0.0 and the first voice packs. 2 adds module v1.1.0 and a newer
    fr-CA pack, so the app sees updates on its next catalog refresh.

.PARAMETER Rebuild
    Regenerates every package. Without it, packages already built are reused, so a pack installed at
    stage 1 is byte-for-byte the one stage 2 lists.

.PARAMETER OutDir
    Where everything is written. Defaults to out\ beside this script (gitignored). site\ under it is
    what serve.cs serves.

.EXAMPLE
    .\scripts\voice-pack-test-kit\build.ps1
    .\scripts\voice-pack-test-kit\build.ps1 -Stage 2
#>
[CmdletBinding()]
param(
    [ValidateSet(1, 2)]
    [int]$Stage = 1,

    [switch]$Rebuild,

    [string]$OutDir = (Join-Path $PSScriptRoot 'out')
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$src = Join-Path $OutDir 'src'
$site = Join-Path $OutDir 'site'
$tag = 'kit'
$packages = Join-Path $site $tag
$pfx = Join-Path $OutDir 'voicekit-signing.pfx'
# A throwaway certificate made by this script for this script; the password protects nothing.
$pfxPassword = 'voicekit'
$moduleId = 'masterwork.voicekit'

# ── Packer ──────────────────────────────────────────────────────────────────

$packerOut = Join-Path $OutDir 'packer'
Write-Host 'Building Masterwork.ModulePacker...'
& dotnet build (Join-Path $repoRoot 'src\Masterwork.ModulePacker\Masterwork.ModulePacker.csproj') -c Release -o $packerOut --nologo -v quiet | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'ModulePacker build failed.' }
$packerDll = Join-Path $packerOut 'Masterwork.ModulePacker.dll'

function Invoke-Packer {
    & dotnet $packerDll @args
    if ($LASTEXITCODE -ne 0) { throw "ModulePacker $($args[0]) failed." }
}

# ── Audio ───────────────────────────────────────────────────────────────────

$audioJobs = [System.Collections.Generic.List[object]]::new()

# Queues one narration file. Each says aloud which pack, track and passage it belongs to, so it's
# obvious by ear which track the app picked.
function Add-Narration([string]$Dir, [string]$Language, [string]$Track, [string]$Slug, [string]$Text) {
    $path = Join-Path $Dir "assets\audio\vo\$Language\$Track\$Slug.wav"
    New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null
    $audioJobs.Add([pscustomobject]@{ Path = $path; Text = $Text })
}

# Speech through Windows' own synthesizer (Windows PowerShell has System.Speech); a tone per file
# where that isn't available.
function Write-QueuedAudio {
    $jobsFile = Join-Path $OutDir 'audio-jobs.json'
    $audioJobs | ConvertTo-Json | Set-Content -LiteralPath $jobsFile -Encoding utf8
    $spoken = $false
    if (Get-Command powershell.exe -ErrorAction SilentlyContinue) {
        $speak = @'
param($jobsFile)
Add-Type -AssemblyName System.Speech
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
foreach ($job in (Get-Content -LiteralPath $jobsFile -Raw | ConvertFrom-Json)) {
    $synth.SetOutputToWaveFile($job.Path)
    $synth.Speak($job.Text)
}
$synth.SetOutputToNull()
'@
        $speakFile = Join-Path $OutDir 'speak.ps1'
        Set-Content -LiteralPath $speakFile -Value $speak -Encoding utf8
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $speakFile $jobsFile 2>$null
        $spoken = $LASTEXITCODE -eq 0 -and ($audioJobs | Where-Object { -not (Test-Path $_.Path) }).Count -eq 0
    }

    if (-not $spoken) {
        Write-Warning 'Speech synthesis unavailable; writing tones instead (a different pitch per file).'
        $i = 0
        foreach ($job in $audioJobs) {
            Write-Tone $job.Path (330 + 55 * ($i++ % 12))
        }
    }
}

function Write-Tone([string]$Path, [double]$Frequency) {
    $rate = 22050; $seconds = 2; $count = $rate * $seconds
    $stream = [System.IO.MemoryStream]::new()
    $w = [System.IO.BinaryWriter]::new($stream)
    $w.Write([Text.Encoding]::ASCII.GetBytes('RIFF')); $w.Write([int](36 + $count * 2))
    $w.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt ')); $w.Write([int]16); $w.Write([int16]1); $w.Write([int16]1)
    $w.Write([int]$rate); $w.Write([int]($rate * 2)); $w.Write([int16]2); $w.Write([int16]16)
    $w.Write([Text.Encoding]::ASCII.GetBytes('data')); $w.Write([int]($count * 2))
    for ($n = 0; $n -lt $count; $n++) {
        $w.Write([int16](8000 * [Math]::Sin(2 * [Math]::PI * $Frequency * $n / $rate)))
    }
    $w.Flush()
    [System.IO.File]::WriteAllBytes($Path, $stream.ToArray())
}

# ── Content ─────────────────────────────────────────────────────────────────

function Write-Text([string]$Path, [string]$Content) {
    New-Item -ItemType Directory -Force (Split-Path $Path) | Out-Null
    [System.IO.File]::WriteAllText($Path, $Content.Replace("`r`n", "`n"), [System.Text.UTF8Encoding]::new($false))
}

$slugs = 'intro', 'middle', 'ending'

# Three passages in a loop, each with one narration audio_track, in en-US and fr-CA text. Its own
# en-US 'builtin' track narrates all three.
function New-Module([string]$Version) {
    $dir = Join-Path $src "module-$Version"
    Write-Text "$dir\manifest.yaml" @"
type: 'module'
format: 'mws/0.6'
id: '$moduleId'
version: '$Version'
languages:
  - 'en-US'
  - 'fr-CA'
title:
  - en-US: 'Voice Pack Test Kit'
  - fr-CA: 'Trousse d''essai des packs de voix'
description:
  - en-US: 'Synthetic module for checking voice pack workflows (v$Version). Not a scenario.'
  - fr-CA: 'Module synthétique pour vérifier les packs de voix (v$Version). Pas un scénario.'
entry: 'KitStart'
voices:
  en-US:
  - id: 'builtin'
    name: 'Built-in reader'
"@
    $passages = [ordered]@{ KitStart = 'KitMiddle'; KitMiddle = 'KitEnding'; KitEnding = 'KitStart' }
    $i = 0
    foreach ($passage in $passages.Keys) {
        Write-Text "$dir\passages\$passage.mws.yaml" @"
format: 'mws/0.6'
passage_id: '$passage'
$(if ($passage -eq 'KitStart') { "tags:`n- 'Begins-Here'" })
title: 'restext://${passage}_Title'
nodes:
- type: 'text'
  value: 'restext://${passage}_Text'
- type: 'audio_track'
  asset: 'audio://vo/$($slugs[$i])'
  title: 'restext://Narration_Title'
- type: 'link'
  label: 'restext://${passage}_Next'
  target: '$($passages[$passage])'
  snapshot: true
"@
        $i++
    }

    Write-Text "$dir\en-US.restext" @"
KitStart_Title=Voice pack test kit
KitStart_Text=Module v$Version. The narration below says which pack and track is playing it.
KitStart_Next=Next
KitMiddle_Title=Middle
KitMiddle_Text=The French pack v1.0.0 has no narration for the next passage, so it should be hidden there.
KitMiddle_Next=Next
KitEnding_Title=Ending
KitEnding_Text=Last passage.
KitEnding_Next=Back to the start
Narration_Title=Narration
"@
    Write-Text "$dir\fr-CA.restext" @"
KitStart_Title=Trousse d'essai des packs de voix
KitStart_Text=Module v$Version. La narration ci-dessous dit quel pack et quelle piste la jouent.
KitStart_Next=Suivant
KitMiddle_Title=Milieu
KitMiddle_Text=Le pack français v1.0.0 n'a pas de narration pour le passage suivant; elle devrait y être masquée.
KitMiddle_Next=Suivant
KitEnding_Title=Fin
KitEnding_Text=Dernier passage.
KitEnding_Next=Retour au début
Narration_Title=Narration
"@
    foreach ($slug in $slugs) {
        Add-Narration $dir 'en-US' 'builtin' $slug "Built-in English reader, module version $Version. Passage: $slug."
    }

    return $dir
}

function New-VoicePack([string]$Name, [string]$Version, [string]$MinModule, [hashtable[]]$Tracks) {
    $dir = Join-Path $src "voices-$Name-$Version"
    $voices = ($Tracks | Group-Object Language | ForEach-Object {
        "  $($_.Name):`n" + (($_.Group | ForEach-Object { "  - id: '$($_.Id)'`n    name: '$($_.Title)'" }) -join "`n")
    }) -join "`n"
    Write-Text "$dir\manifest.yaml" @"
type: 'voices'
id: '$moduleId.voices.$Name'
title: 'Voice Pack Test Kit: $Name narration'
version: '$Version'
for:
  id: '$moduleId'
  min_version: '$MinModule'
voices:
$voices
"@
    foreach ($track in $Tracks) {
        foreach ($slug in $track.Slugs) {
            Add-Narration $dir $track.Language $track.Id $slug "$($track.Say), pack $Name version $Version. Passage: $slug."
        }
    }

    return $dir
}

function Build-Packages {
    if (Test-Path $src) { Remove-Item -Recurse -Force $src }
    New-Item -ItemType Directory -Force $packages | Out-Null

    New-Module '1.0.0' | Out-Null
    $module11 = New-Module '1.1.0'
    $packs = [ordered]@{
        # Its 'builtin' track replaces the module's own; 'female' is a second English track.
        'en-US-1.0.0'    = New-VoicePack 'en-US' '1.0.0' '1.0.0' @(
            @{ Language = 'en-US'; Id = 'builtin'; Title = 'Pack reader (replaces built-in)'; Say = 'English pack reader, replacing the built-in one'; Slugs = $slugs },
            @{ Language = 'en-US'; Id = 'female'; Title = 'Second reader'; Say = 'Second English reader'; Slugs = $slugs })
        # 1.0.0 leaves 'ending' out (a gap); 1.1.0, listed at stage 2, fills it.
        'fr-CA-1.0.0'    = New-VoicePack 'fr-CA' '1.0.0' '1.0.0' @(
            @{ Language = 'fr-CA'; Id = 'lecteur'; Title = 'Lecteur'; Say = 'French reader'; Slugs = @('intro', 'middle') })
        'fr-CA-1.1.0'    = New-VoicePack 'fr-CA' '1.1.0' '1.0.0' @(
            @{ Language = 'fr-CA'; Id = 'lecteur'; Title = 'Lecteur'; Say = 'French reader'; Slugs = $slugs })
        # Needs module 1.1.0, so it's refused while 1.0.0 is installed.
        'cs-CZ-1.0.0'    = New-VoicePack 'cs-CZ' '1.0.0' '1.1.0' @(
            @{ Language = 'cs-CZ'; Id = 'reader'; Title = 'Reader'; Say = 'Czech reader'; Slugs = $slugs })
        # Adds the en-US pack's 'female' track again, so it's refused once that pack is installed.
        'conflict-1.0.0' = New-VoicePack 'conflict' '1.0.0' '1.0.0' @(
            @{ Language = 'en-US'; Id = 'female'; Title = 'Conflicting reader'; Say = 'Conflicting English reader'; Slugs = $slugs })
    }

    Write-Host "Writing $($audioJobs.Count) narration files..."
    Write-QueuedAudio

    if (-not (Test-Path $pfx)) {
        Invoke-Packer gencert 'Masterwork Voice Pack Test Kit' $pfx $pfxPassword 1
    }

    foreach ($version in '1.0.0', '1.1.0') {
        Invoke-Packer module (Join-Path $src "module-$version") $files["module-$version"]
        Invoke-Packer sign $files["module-$version"] $pfx $pfxPassword
    }

    foreach ($key in $packs.Keys) {
        Write-Host "Packing $key (coverage against module 1.1.0):"
        Invoke-Packer voices $packs[$key] $files[$key] --module $module11
        Invoke-Packer sign $files[$key] $pfx $pfxPassword
    }
}

# ── Packages, signing, catalog ──────────────────────────────────────────────

$files = [ordered]@{}
foreach ($version in '1.0.0', '1.1.0') { $files["module-$version"] = Join-Path $packages "voicekit-$version.mwm" }
foreach ($key in 'en-US-1.0.0', 'fr-CA-1.0.0', 'fr-CA-1.1.0', 'cs-CZ-1.0.0', 'conflict-1.0.0') {
    $files[$key] = Join-Path $packages "voicekit-voices-$key.mwvoices"
}

if ($Rebuild -or ($files.Values | Where-Object { -not (Test-Path $_) })) {
    Build-Packages
} else {
    Write-Host 'Reusing the packages already built (pass -Rebuild to regenerate them).'
}

$listed = if ($Stage -eq 1) {
    'module-1.0.0', 'en-US-1.0.0', 'fr-CA-1.0.0', 'cs-CZ-1.0.0', 'conflict-1.0.0'
} else {
    # Both versions are passed; the catalog command lists only the latest of each.
    'module-1.0.0', 'module-1.1.0', 'en-US-1.0.0', 'fr-CA-1.0.0', 'fr-CA-1.1.0', 'cs-CZ-1.0.0', 'conflict-1.0.0'
}

$catalog = Join-Path $site 'catalog.json'
Invoke-Packer catalog $catalog "Voice Pack Test Kit (stage $Stage)" $tag @($listed | ForEach-Object { $files[$_] })
Invoke-Packer signfile $catalog $pfx $pfxPassword

Write-Host ''
Write-Host "Stage $Stage catalog written to $catalog"
Write-Host "Serve it:  dotnet run $(Join-Path $PSScriptRoot 'serve.cs')"
Write-Host 'Then add https://localhost:5443/catalog.json as a catalog source (see README.md).'
