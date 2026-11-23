# PROTOTYPE — throwaway. Builds the sample batch for the translate/verify prototype.
param(
  [string]$Matches = 'C:\Users\dunca\AppData\Local\Temp\claude\46e404daeb976a4c\3d4b558a-74c1-4d94-bbb2-24aaf9ace1f3\scratchpad\p22\out\matches.json',
  [string]$Module = 'C:\Projects\Masterwork-Modules\a-time-of-war',
  [string]$Resx = 'C:\Projects\Masterwork\src\Masterwork.App.Shared\Resources',
  [string]$Out = "$PSScriptRoot\sample.json"
)
$devNote = '(Note$|^VariableList-|^Extra Code$|^DECREESLIST$|^OUTDATEDFirstPara$|^FixIt$|^TWHubExample$)'
$d = Get-Content $Matches -Raw | ConvertFrom-Json
$rows = $d.results | Where-Object { -not ($_.Passages | Where-Object { $_ -match $devNote }) }

# passage -> ordered key list, for context
$passageKeys = @{}
foreach ($dir in 'passages', 'passages-override') {
  foreach ($f in Get-ChildItem "$Module\$dir" -Filter *.mws.yaml) {
    $id = ($f.BaseName -replace '^\d+-', '' -replace '\.mws$', '')
    $passageKeys[$id] = [regex]::Matches((Get-Content $f -Raw), 'restext://([A-Za-z0-9_]+)') | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique
  }
}
$en = @{}
foreach ($f in 'en-US.restext', 'en-US.overrides.restext') {
  Get-Content "$Module\$f" | Where-Object { $_ -match '^[^#\s][^=]*=' } | ForEach-Object { $k, $v = $_ -split '=', 2; $en[$k] = $v }
}

$rand = [Random]::new(23)
function Pick($set, $n) { $set | Sort-Object { $rand.Next() } | Select-Object -First $n }
$picks = @(
  @{ bucket = 'verify'; reason = 'official'; rows = (Pick ($rows | Where-Object Class -eq 'official') 10) }
  @{ bucket = 'verify'; reason = 'spacing'; rows = (Pick ($rows | Where-Object { $_.Flags -contains 'spacing' }) 3) }
  @{ bucket = 'draft'; reason = 'needs-split'; rows = (Pick ($rows | Where-Object Class -eq 'needs-split') 6) }
  @{ bucket = 'draft'; reason = 'placeholders'; rows = (Pick ($rows | Where-Object { $_.Flags -contains 'placeholders' }) 4) }
  @{ bucket = 'draft'; reason = 'length'; rows = (Pick ($rows | Where-Object { $_.Flags -contains 'length' }) 3) }
  @{ bucket = 'draft'; reason = 'conflict'; rows = (Pick ($rows | Where-Object { $_.Flags -like 'conflict*' }) 3) }
  @{ bucket = 'translate'; reason = 'untranslated'; rows = (Pick ($rows | Where-Object { $_.Flags -contains 'untranslated' }) 6) }
  @{ bucket = 'translate'; reason = 'unmatched'; rows = (Pick ($rows | Where-Object Class -eq 'unmatched') 5) }
  @{ bucket = 'translate'; reason = 'override-no-source'; rows = (Pick ($rows | Where-Object Class -eq 'override-no-source') 5) }
)
$items = foreach ($p in $picks) {
  foreach ($r in $p.rows) {
    $pid0 = $r.Passages | Select-Object -First 1
    if (-not $pid0) { $pid0 = $passageKeys.Keys | Where-Object { $passageKeys[$_] -contains $r.Key } | Select-Object -First 1 }
    $ctx = if ($pid0 -and $passageKeys[$pid0]) { $passageKeys[$pid0] | ForEach-Object { [ordered]@{ key = $_; en = $en[$_] } } } else { @() }
    [ordered]@{
      id = $r.Key; surface = 'a-time-of-war'; bucket = $p.bucket; reason = $p.reason
      en = $r.En; fr = $r.Fr; alternatives = $r.Alternatives; splitGroup = $r.SplitGroup
      passage = $pid0; context = @($ctx)
    }
  }
}

# app UI: verify-only sample from AppStrings.fr-CA.resx
[xml]$enX = Get-Content "$Resx\AppStrings.resx" -Raw
[xml]$frX = Get-Content "$Resx\AppStrings.fr-CA.resx" -Raw
$frMap = @{}; foreach ($n in $frX.root.data) { $frMap[$n.name] = $n.value }
$ui = Pick ($enX.root.data | Where-Object { $frMap.ContainsKey($_.name) }) 10
$items += foreach ($n in $ui) {
  [ordered]@{ id = $n.name; surface = 'app-ui'; bucket = 'verify'; reason = 'existing-fr-CA'; en = $n.value; fr = $frMap[$n.name]; alternatives = @(); splitGroup = @(); passage = $null; context = @() }
}
$items | ConvertTo-Json -Depth 6 | Set-Content $Out -Encoding utf8
"wrote $(@($items).Count) items to $Out"
