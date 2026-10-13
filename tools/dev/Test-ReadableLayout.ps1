param([Parameter(Mandatory=$true)][string]$Exe,[Parameter(Mandatory=$true)][string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'
$previous = $env:GPC_LAYOUT_EVIDENCE_DIR
try {
    $env:GPC_LAYOUT_EVIDENCE_DIR = [IO.Path]::GetFullPath($EvidenceDirectory)
    & $Exe --selftest
    if ($LASTEXITCODE -ne 0) { throw "Self-test failed: $LASTEXITCODE" }
    $images = @(Get-ChildItem $EvidenceDirectory -Filter 'layout-*.png')
    if ($images.Count -lt 30) { throw 'Missing RU/EN narrow/short/wide separate findings, actions and security synthetic rendered evidence' }
    $images | Get-FileHash -Algorithm SHA256 | ConvertTo-Json | Set-Content (Join-Path $EvidenceDirectory 'powershell-sha256.json')
} finally { $env:GPC_LAYOUT_EVIDENCE_DIR = $previous }
