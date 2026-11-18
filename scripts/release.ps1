<#
.SYNOPSIS
    Builds a flavor's release artifacts: the portable Windows zip, the Microsoft Store upload, the
    side-load APK and the Google Play bundle.

.DESCRIPTION
    Run it yourself, in a terminal no agent shares: the Android targets ask for signing passwords.
    Each password is read without echo and handed to the build as an environment variable (APK) or a
    temporary file deleted afterwards (AAB, which can't take an environment variable).

    Every Android artifact is checked against the certificate in the keystore it was meant to be
    signed with, because a misconfigured build silently falls back to the debug key.

    Outputs land in artifacts\<version>\, with SHA256SUMS.txt beside them. See docs/release.md.

.EXAMPLE
    ./scripts/release.ps1
    ./scripts/release.ps1 -Targets Store
    ./scripts/release.ps1 -Targets Apk,Aab -SkipTests
#>
[CmdletBinding()]
param(
    [string] $Flavor = 'MyFathersWork',

    [ValidateSet('Windows', 'Store', 'Apk', 'Aab')]
    [string[]] $Targets = @('Windows', 'Store', 'Apk', 'Aab'),

    [ValidateSet('x64', 'arm64', 'x86')]
    [string[]] $StoreArchitectures = @('x64', 'arm64'),

    # Signs the side-load APK. It is also the Play app signing key, so the two channels can update
    # each other's installs.
    [string] $ReleaseKeyStore = 'masterwork-release.keystore',
    [string] $ReleaseKeyAlias = 'masterwork',

    # Signs the bundle uploaded to Play Console. Google re-signs what it distributes with the app
    # signing key; a lost upload key can be reset through Play Console.
    [string] $UploadKeyStore = 'masterwork-upload.keystore',
    [string] $UploadKeyAlias = 'upload',

    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$project = 'src/Masterwork.App/Masterwork.App.csproj'
$windowsTfm = 'net10.0-windows10.0.19041.0'
$androidTfm = 'net10.0-android'

function Get-BuildProperty([string] $File, [string] $Name) {
    if (-not (Test-Path $File)) { return $null }
    $match = Select-String -Path $File -Pattern "<$Name>([^<]+)</$Name>" | Select-Object -First 1
    if ($match) { $match.Matches[0].Groups[1].Value.Trim() } else { $null }
}

# Arguments go in as one array: PowerShell would split a bare -p:Name=value passed through $args.
function Invoke-Dotnet([string[]] $Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE." }
}

function Invoke-Tool([string] $Path, [string[]] $Arguments) {
    $output = & $Path @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "$(Split-Path $Path -Leaf) failed:`n$($output -join "`n")" }
    $output
}

function Read-Password([string] $Prompt) {
    $secure = Read-Host -Prompt $Prompt -AsSecureString
    [System.Net.NetworkCredential]::new('', $secure).Password
}

$version = Get-BuildProperty 'Directory.Build.props' 'VersionPrefix'
$defaultFlavor = Get-BuildProperty 'Directory.Build.props' 'WhiteLabelDefaultFlavor'
$androidVersion = Get-BuildProperty $project 'ApplicationVersion'
if (-not $version) { throw 'VersionPrefix not found in Directory.Build.props.' }

# A non-default flavor builds into bin\<Flavor>\ and obj\<Flavor>\ (Directory.Build.props).
$flavorFolder = if ($Flavor -eq $defaultFlavor) { '' } else { "$Flavor\" }
$appBin = "src\Masterwork.App\bin\$flavorFolder"
$appObj = "src\Masterwork.App\obj\$flavorFolder"
$prefix = if ($Flavor -eq $defaultFlavor) { "Masterwork-$version" } else { "Masterwork-$Flavor-$version" }

$out = Join-Path $root "artifacts\$version"
New-Item -ItemType Directory -Force $out | Out-Null

Write-Host "Release $version ($Flavor), Android versionCode $androidVersion -> $out" -ForegroundColor Cyan
Write-Host "Targets: $($Targets -join ', ')"

if ($Targets -contains 'Windows' -or $Targets -contains 'Store') {
    if (Get-Process Masterwork.App -ErrorAction SilentlyContinue) {
        throw 'Masterwork.App is running. Close it first: it locks the files the Windows publish writes.'
    }
}

if (-not $SkipTests) {
    Invoke-Dotnet @('test', 'src/Masterwork.Tests/Masterwork.Tests.csproj')
}

$localProps = 'Directory.Build.local.props'
$androidSdk = Get-BuildProperty $localProps 'AndroidSdkDirectory'
$javaSdk = Get-BuildProperty $localProps 'JavaSdkDirectory'

# The first SHA-256 certificate fingerprint in keytool or apksigner output, as lowercase hex.
function Find-Sha256([string[]] $Output) {
    $match = [regex]::Match(($Output -join "`n"), '(?m)(?:^\s*SHA256:|certificate SHA-256 digest:)\s*([0-9A-Fa-f:]+)')
    if ($match.Success) { ($match.Groups[1].Value -replace ':', '').ToLowerInvariant() } else { $null }
}

function Get-CertificateDigest([string] $KeyStore, [string] $Alias, [string] $PasswordVariable) {
    $keytool = Join-Path $javaSdk 'bin\keytool.exe'
    $digest = Find-Sha256 (Invoke-Tool $keytool @('-list', '-v', '-keystore', $KeyStore, '-alias', $Alias, '-storepass:env', $PasswordVariable))
    if (-not $digest) { throw "No SHA-256 fingerprint in $KeyStore ($Alias)." }
    $digest
}

function Clear-AndroidOutput {
    # A stale incremental build can skip re-signing and still produce a plausible-looking file.
    foreach ($folder in "$appBin\Release\$androidTfm", "$appObj\Release\$androidTfm") {
        if (Test-Path $folder) { Remove-Item $folder -Recurse -Force }
    }
}

# --- Portable Windows zip -------------------------------------------------------------------------

if ($Targets -contains 'Windows') {
    Invoke-Dotnet @('publish', $project, '-f', $windowsTfm, '-c', 'Release',
        '-p:RuntimeIdentifierOverride=win-x64', '-p:WindowsPackageType=None', '-p:WindowsAppSDKSelfContained=true',
        '-p:BuildAndroid=false', "-p:WhiteLabelFlavor=$Flavor")

    $zip = Join-Path $out "$prefix-win-x64.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path "$appBin\Release\$windowsTfm\win-x64\publish\*" -DestinationPath $zip
    Write-Host "Windows: $zip" -ForegroundColor Green
}

# --- Microsoft Store upload -----------------------------------------------------------------------

if ($Targets -contains 'Store') {
    $nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $HOME '.nuget\packages' }
    $makeAppx = Get-ChildItem (Join-Path $nugetRoot 'microsoft.windows.sdk.buildtools') -Recurse -Filter makeappx.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.Directory.Name -eq 'x64' } | Sort-Object FullName | Select-Object -Last 1
    if (-not $makeAppx) { throw 'makeappx.exe not found: build the Windows target once so NuGet restores Microsoft.Windows.SDK.BuildTools.' }

    $storeBin = "$appBin\Store"
    $packages = "$storeBin\AppPackages"
    if (Test-Path $packages) { Remove-Item $packages -Recurse -Force }

    foreach ($architecture in $StoreArchitectures) {
        Invoke-Dotnet @('publish', $project, '-f', $windowsTfm, '-c', 'Release',
            "-p:RuntimeIdentifierOverride=win-$architecture", '-p:StorePackage=true',
            '-p:BuildAndroid=false', "-p:WhiteLabelFlavor=$Flavor")
    }

    $staging = Join-Path $out 'store-staging'
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
    New-Item -ItemType Directory $staging | Out-Null
    foreach ($architecture in $StoreArchitectures) {
        $msix = Get-ChildItem $packages -Recurse -Filter "*_$version.0_$architecture.msix" | Select-Object -First 1
        if (-not $msix) { throw "No $architecture MSIX for $version.0 under $packages." }
        Copy-Item $msix.FullName $staging
    }

    $bundle = Join-Path $out "$prefix.msixbundle"
    Invoke-Tool $makeAppx.FullName @('bundle', '/bv', "$version.0", '/d', $staging, '/p', $bundle, '/o') | Out-Null

    # An .msixupload is a zip of the bundle (and, when present, its symbols).
    $upload = Join-Path $out "$prefix.msixupload"
    $uploadZip = "$upload.zip"
    foreach ($file in $upload, $uploadZip) { if (Test-Path $file) { Remove-Item $file -Force } }
    Compress-Archive -Path $bundle -DestinationPath $uploadZip
    Move-Item $uploadZip $upload
    Remove-Item $staging -Recurse -Force
    Write-Host "Store: $upload" -ForegroundColor Green
}

# --- Android ----------------------------------------------------------------------------------------

if ($Targets -contains 'Apk' -or $Targets -contains 'Aab') {
    if (-not $androidSdk -or -not $javaSdk) { throw "Set AndroidSdkDirectory and JavaSdkDirectory in $localProps." }
}

if ($Targets -contains 'Apk') {
    if (-not (Test-Path $ReleaseKeyStore)) { throw "Release keystore not found: $ReleaseKeyStore" }
    $env:MwReleaseKeyPass = Read-Password "Password for $ReleaseKeyStore"
    try {
        $expected = Get-CertificateDigest $ReleaseKeyStore $ReleaseKeyAlias 'MwReleaseKeyPass'
        Clear-AndroidOutput
        Invoke-Dotnet @('publish', $project, '-f', $androidTfm, '-c', 'Release', "-p:WhiteLabelFlavor=$Flavor",
            '-p:AndroidKeyStore=true', '-p:AndroidPackageFormats=apk',
            "-p:AndroidSigningKeyStore=$(Resolve-Path $ReleaseKeyStore)", "-p:AndroidSigningKeyAlias=$ReleaseKeyAlias",
            '-p:AndroidSigningKeyPass=env:MwReleaseKeyPass', '-p:AndroidSigningStorePass=env:MwReleaseKeyPass')
    }
    finally {
        Remove-Item env:MwReleaseKeyPass -ErrorAction SilentlyContinue
    }

    $apk = Get-ChildItem "$appBin\Release\$androidTfm\publish" -Filter '*-Signed.apk' | Select-Object -First 1
    if (-not $apk) { throw 'No signed APK in the publish output.' }

    $apkSigner = Get-ChildItem (Join-Path $androidSdk 'build-tools') -Recurse -Filter apksigner.bat | Sort-Object FullName | Select-Object -Last 1
    $certificates = Invoke-Tool $apkSigner.FullName @('verify', '--print-certs', $apk.FullName)
    if ((Find-Sha256 $certificates) -ne $expected) {
        throw "The APK isn't signed with $ReleaseKeyStore.`n$($certificates -join "`n")"
    }

    $apkOut = Join-Path $out "$prefix-android.apk"
    Copy-Item $apk.FullName $apkOut -Force
    Write-Host "APK: $apkOut (signed with $ReleaseKeyStore)" -ForegroundColor Green
}

if ($Targets -contains 'Aab') {
    if (-not (Test-Path $UploadKeyStore)) {
        throw "Upload keystore not found: $UploadKeyStore. Create it once with keytool; see docs/release.md."
    }
    $env:MwUploadKeyPass = Read-Password "Password for $UploadKeyStore"
    $passwordFile = Join-Path ([System.IO.Path]::GetTempPath()) ([System.IO.Path]::GetRandomFileName())
    try {
        $expected = Get-CertificateDigest $UploadKeyStore $UploadKeyAlias 'MwUploadKeyPass'
        [System.IO.File]::WriteAllText($passwordFile, $env:MwUploadKeyPass)
        Clear-AndroidOutput
        Invoke-Dotnet @('publish', $project, '-f', $androidTfm, '-c', 'Release', "-p:WhiteLabelFlavor=$Flavor",
            '-p:AndroidKeyStore=true', '-p:AndroidPackageFormats=aab',
            "-p:AndroidSigningKeyStore=$(Resolve-Path $UploadKeyStore)", "-p:AndroidSigningKeyAlias=$UploadKeyAlias",
            "-p:AndroidSigningKeyPass=file:$passwordFile", "-p:AndroidSigningStorePass=file:$passwordFile")
    }
    finally {
        Remove-Item $passwordFile -Force -ErrorAction SilentlyContinue
        Remove-Item env:MwUploadKeyPass -ErrorAction SilentlyContinue
    }

    $aab = Get-ChildItem "$appBin\Release\$androidTfm\publish" -Filter '*-Signed.aab' | Select-Object -First 1
    if (-not $aab) { throw 'No signed AAB in the publish output.' }

    $keytool = Join-Path $javaSdk 'bin\keytool.exe'
    $printed = Invoke-Tool $keytool @('-printcert', '-jarfile', $aab.FullName)
    if ((Find-Sha256 $printed) -ne $expected) {
        throw "The AAB isn't signed with $UploadKeyStore.`n$($printed -join "`n")"
    }

    $aabOut = Join-Path $out "$prefix-android.aab"
    Copy-Item $aab.FullName $aabOut -Force
    Write-Host "AAB: $aabOut (signed with $UploadKeyStore, for Play Console only)" -ForegroundColor Green
}

# --- Checksums --------------------------------------------------------------------------------------

Get-ChildItem $out -File | Where-Object Name -ne 'SHA256SUMS.txt' | Sort-Object Name | ForEach-Object {
    "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($_.Name)"
} | Set-Content (Join-Path $out 'SHA256SUMS.txt') -Encoding ascii

Write-Host "Done. Artifacts and SHA256SUMS.txt are in $out" -ForegroundColor Cyan
