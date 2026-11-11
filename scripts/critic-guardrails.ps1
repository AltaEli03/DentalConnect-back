#Requires -Version 5.1
<#
.SYNOPSIS
  Checks 1-2 del security-reviewer de forma determinista: TODO y secretos.
.DESCRIPTION
  Revisa archivos agregados o modificados contra -Base (y el working tree).
  Exit 0 = PASS, 1 = FAIL, 2 = error de uso. Excepcion en una linea: "critic-allow: secret".
  ASCII puro. Compatible con PowerShell 5.1 y pwsh.
#>
param([string]$Base = 'origin/main')
$ErrorActionPreference = 'Stop'

git rev-parse --verify --quiet $Base | Out-Null
if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine('critic-guardrails: no existe ' + $Base)
    exit 2
}

$extRx  = '\.(cs|csproj|props|config|ts|tsx|js|jsx|json|ya?ml|ps1|sql)$'
$skipRx = '(^|/)(node_modules|bin|obj|dist|\.git|\.agents)/|^scripts/critic-guardrails\.ps1$|package-lock\.json$'

$files = @(git diff --name-only --diff-filter=AM "$Base...HEAD") + @(git diff --name-only --diff-filter=AM HEAD) |
    Sort-Object -Unique

$secretRules = @(
    @{ Name = 'llave privada';                    Rx = '-----BEGIN [A-Z ]*PRIVATE KEY-----' },
    @{ Name = 'AWS access key';                   Rx = 'AKIA[0-9A-Z]{16}' },
    @{ Name = 'JWT literal';                      Rx = 'eyJ[A-Za-z0-9_-]{10,}\.eyJ[A-Za-z0-9_-]{10,}' },
    @{ Name = 'connection string con password';  Rx = '(?i)(password|pwd)\s*=\s*[^;"\s{$][^;"]*' },
    @{ Name = 'credencial fija';                  Rx = '(?i)(secret|api[_-]?key|token|passwd|password)"?\s*[:=]\s*"[^"{$][^"]{7,}"' }
)

$findings = New-Object System.Collections.Generic.List[string]

foreach ($f in $files) {
    $norm = $f -replace '\\', '/'
    if ($norm -match $skipRx) { continue }

    if ($norm -match '(^|/)\.env(\.[^/]+)?$' -and $norm -notmatch '\.env\.example$') {
        $findings.Add("FAIL | 2 | ${norm}:1 | archivo .env versionado (solo .env.example)")
        continue
    }
    if ($norm -notmatch $extRx) { continue }
    if (-not (Test-Path -LiteralPath $f)) { continue }

    $n = 0
    foreach ($line in (Get-Content -LiteralPath $f)) {
        $n++
        if ($line -match 'critic-allow: secret') { continue }

        if ($line -cmatch '\bTODO\b') {
            $findings.Add("FAIL | 1 | ${norm}:$n | TODO (usar NOTE (DENT-NN))")
        }
        foreach ($rule in $secretRules) {
            if ($line -match $rule.Rx) {
                $findings.Add("FAIL | 2 | ${norm}:$n | " + $rule.Name)
                break
            }
        }
    }
}

if ($findings.Count -eq 0) {
    Write-Output ('PASS - ' + @($files).Count + ' archivos revisados contra ' + $Base)
    exit 0
}
Write-Output ('FAIL - ' + $findings.Count + ' hallazgos')
Write-Output 'resultado | check | ruta:linea | motivo'
$findings | ForEach-Object { Write-Output $_ }
exit 1