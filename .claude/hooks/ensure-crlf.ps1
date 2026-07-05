<#
.SYNOPSIS
    PostToolUse hook (Write|Edit) that normalizes line endings to CRLF.

.DESCRIPTION
    Reads the tool-call JSON from stdin, and for .cs/.csproj/.slnx/.sln files, inserts a CR
    before any LF that doesn't already have one. Operates on raw bytes so encoding (including
    a BOM, if present) is preserved exactly — this is not a text re-encode, just a byte patch.
    Idempotent: re-running on an already-CRLF file is a no-op.
#>

$ErrorActionPreference = 'Stop'

$json = [Console]::In.ReadToEnd() | ConvertFrom-Json
$path = $json.tool_input.file_path
if (-not $path) { exit 0 }
if ($path -notmatch '\.(cs|csproj|slnx|sln)$') { exit 0 }
if (-not (Test-Path -LiteralPath $path)) { exit 0 }

$bytes = [System.IO.File]::ReadAllBytes($path)
$stream = New-Object System.IO.MemoryStream
for ($i = 0; $i -lt $bytes.Length; $i++) {
    $b = $bytes[$i]
    if ($b -eq 10 -and ($i -eq 0 -or $bytes[$i - 1] -ne 13)) {
        $stream.WriteByte(13)
    }
    $stream.WriteByte($b)
}

$newBytes = $stream.ToArray()
if ($newBytes.Length -ne $bytes.Length) {
    [System.IO.File]::WriteAllBytes($path, $newBytes)
}
