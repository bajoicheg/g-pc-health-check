# CDC 2.3.9 completion gate check
# Diagnostic-only policy enforcement. Does not grant ownership or bypass CI.

param(
    [string]$StateFile = ""
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($StateFile)) {
    throw "StateFile is required"
}

$state = Get-Content -Raw -LiteralPath $StateFile | ConvertFrom-Json

$diagnosticOnly = @(
    'status_read',
    'health_check',
    'lease_check',
    'poll_without_transition',
    'report_only'
)

if ($state.runnable_next_action -and -not $state.blocker -and -not $state.external_wait) {
    $hasProgress = @($state.actions | Where-Object { $diagnosticOnly -notcontains $_ }).Count -gt 0
    if (-not $hasProgress) {
        throw 'primitive_only_completion'
    }
}

Write-Host 'CDC_2_3_9_COMPLETION_GATE_GREEN'
