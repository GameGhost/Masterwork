# PROTOTYPE — throwaway. Deterministic stages of the translate/verify process.
#   -Stage assemble : sample.json + forward.json -> candidates.json (normalized French + integrity/term checks)
#   -Stage report   : candidates.json + backtranslation.json + judge.json -> report.md
param([ValidateSet('assemble', 'report', 'scan')][string]$Stage, [string]$Dir = $PSScriptRoot, [string]$MatchesFile)
$NB = [char]0x00A0
function ReadJson($name) { Get-Content (Join-Path $Dir $name) -Raw -Encoding utf8 | ConvertFrom-Json }

# Apply fr-CA typography outside {placeholders}.
function Normalize([string]$s) {
  if (-not $s) { return $s }
  $parts = [regex]::Split($s, '(\{[^}]*\})')
  $out = foreach ($p in $parts) {
    if ($p -match '^\{.*\}$') { $p; continue }
    $p = [regex]::Replace($p, '[ \u00A0\u202F]+([?!;])', '$1')
    $p = [regex]::Replace($p, '(?<=[^\s\u00A0/:\d])[ \u202F]?:(?!//)', "${NB}:")
    $p = [regex]::Replace($p, '(?<=\S)\u00A0?[ \u202F]:', "${NB}:")
    $p = [regex]::Replace($p, '«[ \u00A0\u202F]*', "«${NB}")
    $p = [regex]::Replace($p, '[ \u00A0\u202F]*»', "${NB}»")
    $p
  }
  -join $out
}

function Tokens([string]$s, [string]$pattern) { ([regex]::Matches("$s", $pattern) | ForEach-Object Value | Sort-Object) -join '|' }
function Strip([string]$s) { [regex]::Replace("$s", '\{[^}]*\}', '') }

# English term -> required French pattern, and French forms that are never allowed.
$terms = @(
  @('\bRounds?\b', 'manche'), @('\bStorybook\b', 'Livret Histoire'), @('\bMasterworks?\b', 'Grand Œuvre'),
  @('\bEstate Upgrades?\b', 'Aménagement'), @('\bVPs?\b', '\bPV\b'), @('\bExperiments?\b', 'Expérience|Expériment'),
  @('\bKnowledge\b', 'Connaissance'), @('\bServants?\b', 'Domestique'), @('\bAsylum\b', 'Asile'),
  @('\bGenerations?\b', 'Génération|GÉNÉRATION'), @('\btokens?\b', 'jeton'), @('\bEarly Years\b', 'Début de cycle'),
  @('\bMiddle Years\b', 'Milieu de cycle'), @('\bLate Years\b', 'Fin de cycle'), @('\bIngredients?\b', 'Ingrédient'),
  @('\bAngry Mob\b', 'Foule en colère'), @('\bAchievements?\b', 'Réussite'), @("My Father's Work", 'Le Secret de mon père|My Father''s Work')
)
$forbidden = 'Storybook|Chef-d.œuvre|Grand OEuvre|Réalisations|[Ff]in de tour|premières années|années intermédiaires'

function Check($en, $fr, $surface) {
  $issues = @()
  if (-not $fr) { return @('missing') }
  if ((Tokens $en '\{[^}]*\}') -ne (Tokens $fr '\{[^}]*\}')) { $issues += 'placeholders' }
  foreach ($m in @('\*\*', '//')) { if ([regex]::Matches((Strip $en), $m).Count -ne [regex]::Matches((Strip $fr), $m).Count) { $issues += "markup $($m -replace '\\','')" } }
  if ([regex]::Matches((Strip $en), '(?<![\w])_|_(?![\w])').Count -ne [regex]::Matches((Strip $fr), '(?<![\w])_|_(?![\w])').Count) { $issues += 'markup _' }
  if ($surface -eq 'app-ui' -and (Tokens $en '</?\w+[^>]*>') -ne (Tokens $fr '</?\w+[^>]*>')) { $issues += 'html tags' }
  # A placeholder or closing marker glued to the next word where the English has a space.
  $gluedEn = [regex]::Matches("$en", '(\}|\*\*)(?=\p{L})').Count; $gluedFr = [regex]::Matches("$fr", '(\}|\*\*)(?=\p{L})').Count
  if ($gluedFr -gt $gluedEn) { $issues += 'lost space after placeholder/markup' }
  if ($fr -match '(^|\s)_\s' -and $en -notmatch '(^|\s)_\s') { $issues += 'space inside italic' }
  if ($fr -match "[\r\n]") { $issues += 'multi-line' }
  if ($fr -match '\\') { $issues += 'backslash' }
  if (($en -match '^\s') -ne ($fr -match '^\s') -or ($en -match '\s$') -ne ($fr -match '\s$')) { $issues += 'edge whitespace' }
  if ((Normalize $fr) -cne $fr) { $issues += 'typography' }
  $plainEn = Strip $en; $plainFr = Strip $fr
  if ($surface -ne 'app-ui') {
    foreach ($t in $terms) { if ($plainEn -match $t[0] -and $plainFr -notmatch $t[1]) { $issues += "term: $($t[0] -replace '\\b|\?','') -> $($t[1])" } }
    if ($plainFr -match $forbidden) { $issues += "forbidden term: $($Matches[0])" }
  }
  if ($plainEn -match '[A-Za-z]{3}' -and $plainFr -ceq $plainEn) { $issues += 'still English' }
  $ratio = if ($plainEn.Length) { $plainFr.Length / $plainEn.Length } else { 1 }
  if ($plainEn.Length -gt 20 -and ($ratio -lt 0.7 -or $ratio -gt 2.0)) { $issues += ('length x{0:N2}' -f $ratio) }
  $issues
}

if ($Stage -eq 'assemble') {
  $sample = ReadJson 'sample.json'
  $fwd = @{}; foreach ($f in (ReadJson 'forward.json')) { $fwd[$f.id] = $f }
  $cands = foreach ($s in $sample) {
    if ($s.bucket -eq 'verify') { $raw = $s.fr; $origin = if ($s.surface -eq 'app-ui') { 'existing' } else { 'official' }; $note = '' }
    else { $raw = $fwd[$s.id].fr; $origin = $fwd[$s.id].origin; $note = $fwd[$s.id].note }
    $fr = Normalize $raw
    [ordered]@{ id = $s.id; surface = $s.surface; bucket = $s.bucket; reason = $s.reason; en = $s.en; official = $s.fr
      fr = $fr; origin = $origin; normalized = ($fr -cne $raw); forwardNote = $note; checks = @(Check $s.en $fr $s.surface); passage = $s.passage }
  }
  $cands | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $Dir 'candidates.json') -Encoding utf8
  # The blind back-translator sees only French: id, French, and the French of neighbouring sample strings in the same passage.
  $cands | ForEach-Object { [ordered]@{ id = $_.id; surface = $_.surface; passage = $_.passage; fr = $_.fr } } | ConvertTo-Json | Set-Content (Join-Path $Dir 'blind-input.json') -Encoding utf8
  $n = @($cands | Where-Object { $_.checks.Count }).Count
  "assembled $(@($cands).Count) candidates; $n with check findings"
  $cands | Where-Object { $_.checks.Count } | ForEach-Object { "  $($_.id): $($_.checks -join '; ')" }
}

if ($Stage -eq 'report') {
  $cands = ReadJson 'candidates.json'
  $bt = @{}; foreach ($b in (ReadJson 'backtranslation.json')) { $bt[$b.id] = $b.en }
  $jd = @{}; foreach ($j in (ReadJson 'judge.json')) { $jd[$j.id] = $j }
  $rev = @{}; if (Test-Path (Join-Path $Dir 'revisions.json')) { foreach ($r in (ReadJson 'revisions.json')) { $rev[$r.id] = $r } }
  function Esc($s) { "$s" -replace '\|', '\|' -replace [char]0x00A0, '⍽' }
  $L = [System.Collections.Generic.List[string]]::new()
  $L.Add('# PROTOTYPE fr-CA verification report — sample'); $L.Add('')
  $L.Add('`⍽` marks a non-breaking space. Verdicts: **pass**, **fail: meaning**, **fail: typo**, **fail: term**, **fail: integrity**. A fail sends the string back for one revision round; a second fail goes to "Unresolved".'); $L.Add('')
  $rows = foreach ($c in $cands) {
    $j = $jd[$c.id]; $r = $rev[$c.id]
    $final = if ($r) { $r.fr } else { $c.fr }
    $finalOrigin = if ($r -and $c.origin -in 'official', 'existing') { if ($c.origin -eq 'official') { 'official, revised' } else { 'existing, revised' } } else { $c.origin }
    $verdict = if ($r) { if ($r.verdict) { "$($j.verdict) → revised → $($r.verdict)" } else { "$($j.verdict) → revised" } } else { $j.verdict }
    [pscustomobject]@{ c = $c; j = $j; r = $r; final = $final; origin = $finalOrigin; verdict = $verdict; checks = @(Check $c.en $final $c.surface) }
  }
  $L.Add('## Summary'); $L.Add('')
  $L.Add('| Bucket | Strings | Pass first time | Revised | Unresolved | Typography normalized |'); $L.Add('|---|---|---|---|---|---|')
  foreach ($g in $rows | Group-Object { "$($_.c.surface) / $($_.c.bucket)" }) {
    $p = @($g.Group | Where-Object { $_.j.verdict -eq 'pass' -and -not $_.r }).Count
    $rv = @($g.Group | Where-Object { $_.r -and $_.r.verdict -like 'pass*' }).Count
    $u = @($g.Group | Where-Object { $_.r -and $_.r.verdict -notlike 'pass*' }).Count
    $nz = @($g.Group | Where-Object { $_.c.normalized }).Count
    $L.Add("| $($g.Name) | $($g.Count) | $p | $rv | $u | $nz |")
  }
  $L.Add('')
  foreach ($section in @(@('Changed or failed', { $_.r -or $_.j.verdict -ne 'pass' }), @('Passed unchanged', { -not $_.r -and $_.j.verdict -eq 'pass' }))) {
    $L.Add("## $($section[0])"); $L.Add('')
    foreach ($x in $rows | Where-Object $section[1]) {
      $c = $x.c
      $L.Add("### ``$($c.id)`` — $($c.surface), $($c.bucket) ($($c.reason))"); $L.Add('')
      $L.Add("- **Origin:** $($x.origin)")
      $L.Add("- **English:** $(Esc $c.en)")
      if ($c.official -and $c.bucket -ne 'verify') { $L.Add("- **Official French (input):** $(Esc $c.official)") }
      $L.Add("- **French:** $(Esc $c.fr)")
      $L.Add("- **Back-translation:** $(Esc $bt[$c.id])")
      $L.Add("- **Verdict:** $($x.verdict)$(if ($x.j.reason) { " — $(Esc $x.j.reason)" })")
      if ($x.r) { $L.Add("- **Revised French:** $(Esc $x.r.fr)"); if ($x.r.backtranslation) { $L.Add("- **Revised back-translation:** $(Esc $x.r.backtranslation)") } }
      $L.Add("- **Checks:** $(if ($x.checks.Count) { $x.checks -join '; ' } else { 'clean' })$(if ($c.normalized) { ' (typography normalized)' })")
      if ($c.forwardNote) { $L.Add("- **Translator note:** $(Esc $c.forwardNote)") }
      $L.Add('')
    }
  }
  $L | Set-Content (Join-Path $Dir 'report.md') -Encoding utf8
  "report: $(@($rows).Count) strings"
}

if ($Stage -eq 'scan') {
  # Whole-scenario estimate: what the checks say about every official 1:1 string.
  $devNote = '(Note$|^VariableList-|^Extra Code$|^DECREESLIST$|^OUTDATEDFirstPara$|^FixIt$|^TWHubExample$)'
  $rows = (Get-Content $MatchesFile -Raw | ConvertFrom-Json).results | Where-Object { $_.Class -in 'official', 'official-flagged' -and $_.Fr -and -not ($_.Flags -contains 'untranslated') -and -not ($_.Passages | Where-Object { $_ -match $devNote }) }
  $tally = @{}; $norm = 0
  foreach ($r in $rows) {
    $fr = Normalize $r.Fr; if ($fr -cne $r.Fr) { $norm++ }
    foreach ($i in @(Check $r.En $fr 'module')) { $k = $i -replace 'length x.*', 'length'; $tally[$k] = 1 + $tally[$k] }
  }
  "official strings scanned: $(@($rows).Count); typography normalized: $norm"
  $tally.GetEnumerator() | Sort-Object Value -Descending | ForEach-Object { '{0,5}  {1}' -f $_.Value, $_.Key }
}
