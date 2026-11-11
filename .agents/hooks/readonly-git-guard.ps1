#Requires -Version 5.1
<#
.SYNOPSIS
  Hook PreToolUse para los sub-agentes de solo lectura (security-reviewer, traceability-clerk).
.DESCRIPTION
  Lee el payload JSON de PreToolUse por stdin y permite UNICAMENTE comandos
  "git diff", "git log" o "git show" sin encadenar, sin redirigir y sin opciones que
  escriban archivos o ejecuten programas externos. Cualquier otro comando: exit 2 + motivo
  en stderr (el agente bloquea la llamada). Compatible con PowerShell 5.1 y pwsh. ASCII puro.
#>
$ErrorActionPreference = 'Stop'

function Block([string]$reason) {
    [Console]::Error.WriteLine('readonly-git-guard: BLOQUEADO - ' + $reason + ' (solo se permite git diff | git log | git show)')
    exit 2
}

$raw = [Console]::In.ReadToEnd()
try {
    $payload = $raw | ConvertFrom-Json
} catch {
    Block 'payload JSON invalido'
}

$tool = [string]$payload.tool_name
if ($tool -ne 'Bash' -and $tool -ne 'PowerShell') {
    exit 0
}

$cmd = ([string]$payload.tool_input.command).Trim()
if ($cmd -eq '') { Block 'comando vacio' }
if ($cmd -match '[;&|<>`\r\n]' -or $cmd -match '\$\(' -or $cmd -match '\$\{') {
    Block 'encadenamiento, redireccion o sustitucion de comandos'
}
if ($cmd -notmatch '^git\s+(diff|log|show)(\s|$)') {
    Block ('comando no permitido: ' + $cmd)
}
if ($cmd -match '(^|\s)--(output|ext-diff|textconv)\b' -or $cmd -match '(^|\s)-c\s') {
    Block 'opcion que escribe archivos, ejecuta programas o cambia configuracion'
}
exit 0
