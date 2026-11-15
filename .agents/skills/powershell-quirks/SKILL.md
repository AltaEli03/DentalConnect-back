---
name: powershell-quirks
description: Escribir scripts PowerShell de DentalConnect que funcionen igual en Windows PowerShell 5.1 y en pwsh 7 (local y CI Linux) sin falsos PASS. Úsala al crear o modificar cualquier .ps1 (hooks de `.agents/`, extractores de guardarraíles) o un bloque `shell: pwsh` de CI.
---

## Propósito
Evitar falsos negativos o positivos por diferencias entre PowerShell 5.1 (Windows) y pwsh (CI en ubuntu).

## Cuándo usarla
Al escribir o modificar `.agents/hooks/*.ps1`, un futuro script extractor (skill `drift-guards`) o un `run:` con `shell: pwsh` en los workflows.

## Precondiciones
Probar en los dos: `powershell -NoProfile -ExecutionPolicy Bypass -File <script>` y `pwsh -NoProfile -File <script>`.

## Pasos
1. **ASCII puro** en los `.ps1`: PS 5.1 lee como ANSI un UTF-8 sin BOM y los acentos se corrompen. Los marcadores con acento se buscan con regex (`Secci.n`).
2. `#Requires -Version 5.1`; nada de `?.`, `??`, ternarios ni `ForEach-Object -Parallel`.
3. **Git dentro de scripts:** helper `Invoke-Git` con `2>&1` + filtro de `ErrorRecord` y `$ErrorActionPreference='Continue'` local; consultar `$LASTEXITCODE` después.
4. **Rutas:** `Join-Path` y `-LiteralPath`; normalizar `\` → `/` antes de comparar rutas de git (los dos repos corren en Windows con git de Git Bash).
5. **Arrays:** envolver con `@(...)`, porque `Get-Content` de una línea devuelve un string y no un array.
6. **Salida:** `exit 0` (PASS) / `exit 1` (FAIL) / `exit 2` (hook: bloquear); mensajes con prefijo del guard (`readonly-git-guard:`, `endpoints:`, …).
7. **Hooks de agente con soporte de `PreToolUse`:** leer stdin con `[Console]::In.ReadToEnd()`; en el frontmatter, `shell: powershell` + `$input | powershell -NoProfile -ExecutionPolicy Bypass -File "…"; exit $LASTEXITCODE` (la política de ejecución por defecto de Windows bloquea los `.ps1`).
8. `Invoke-WebRequest` siempre con `-UseBasicParsing` en 5.1.
9. Fines de línea: fijo `*.ps1 eol=crlf` en `.gitattributes` (5.1 acepta LF, pero CRLF evita diffs ruidosos en Windows).

## Checklist
Probado en 5.1 y en pwsh · ASCII · exit codes · self-test.

## Criterios de done
Misma salida y mismo exit code en `powershell` y `pwsh` sobre el hook real (`readonly-git-guard.ps1`) con payloads de ejemplo: `git diff` permitido; `git diff; rm -rf`, `git show > f`, `git log --output=x` bloqueados.

## Límites/trampas
- `status 0` no implica OK: verificar con un grupo de control.
- En Git Bash, `/p:` de `dotnet` se convierte en una ruta: usar `-p:`.
- Los comandos del front (`npm run test`, `npm run lint`) no necesitan pwsh: este skill aplica solo a PowerShell.

## Referencias
`.agents/hooks/readonly-git-guard.ps1`, `.agents/agents/README.md`, skill `drift-guards`.
