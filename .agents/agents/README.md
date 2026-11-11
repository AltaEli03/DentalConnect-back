# Sub-agentes de DentalConnect (convención híbrida agentes → skills)

Cobertura de los dos repos del proyecto:

- `DentalConnect-back` — API .NET 10 (`net10.0`): Minimal APIs (`DentalConnect/*Endpoints.cs`), Clean Architecture (`DentalConnect.Domain` → `DentalConnect.Application` → `DentalConnect.Infrastructure` → `DentalConnect`), EF Core + PostgreSQL 16, JWT con Cognito, xUnit 2.9 en `DentalConnect.Tests`.
- `DentalConnect-front` — React 19 + Vite + TypeScript: `src/auth` (Cognito), `src/pages`, `src/components`, `src/services`, `src/routes`, vitest + testing-library.

Convención:

- Los agentes definen **rol, permisos y criterio de done**, y actúan distinto según el repo donde se trabaje (back o front).
- Las reglas técnicas viven en `.agents/skills/<nombre>/SKILL.md`; los agentes **solo las enlazan, nunca las copian**. Si una skill cambia, el agente no se toca.

| Agente | Rol | Permisos | Cómo se imponen |
|---|---|---|---|
| `slice-scaffolder` | Fase A: esqueleto compilable de una HU en el repo activo (back: entidad → DTO/reglas → EF → endpoint → pruebas; front: tipos → servicio → componente → prueba) | Lectura, edición y bash | `tools` en el frontmatter |
| `security-reviewer` | Gate pre-push: critica el diff contra `origin/main` (OWASP API Security Top 10 2023 + checks de back o front) | Solo lectura + `git diff/log/show` | `disallowedTools: Write, Edit, NotebookEdit` + hook `PreToolUse` → `.agents/hooks/readonly-git-guard.ps1` |
| `traceability-clerk` | Verifica Jira ↔ spec ↔ código ↔ test y reporta drift | Solo lectura + `git diff/log/show` | Igual que `security-reviewer` |

## Puerta obligatoria: Jira vía Atlassian MCP (nombres de tareas)

**Antes de cualquier acción relacionada con el proyecto** (crear o renombrar una spec, abrir rama, implementar una HU, revisar un diff, cerrar un PR), el nombre de la tarea se **delega al tablero de Jira**: cada HU es un issue `DENT-NN` en `https://uttt-proyecto.atlassian.net` y su *summary* es la única fuente del nombre. Nunca se inventa ni se copia de memoria.

1. Leer el issue con las herramientas del MCP de Atlassian (buscar por clave `DENT-NN`) y usar su summary en la cabecera de la spec, el slug de la carpeta, el título del PR y los commits.
2. Verificación rápida de la puerta: pide al MCP el issue `DENT-13`. Si la herramienta Atlassian no aparece o falla la autenticación, **no se toca el proyecto**: hay que ayudar al usuario a conectar el MCP según su agente:
   - **OpenCode:** `opencode.json` en la raíz del repo (los dos repos de DentalConnect ya lo traen):
     ```json
     { "mcp": { "servers": { "atlassian": { "type": "remote", "url": "https://mcp.atlassian.com/v2/mcp" } } } }
     ```
     El primer uso abre el flujo de login de Atlassian (OAuth); reiniciar el agente si hace falta.
   - **Agentes que leen un `.mcp.json` en la raíz del proyecto (configuración MCP estándar):** escribe ese archivo con el servidor remoto y reinicia la sesión para autenticar (OAuth):
     ```json
     { "mcpServers": { "atlassian": { "type": "http", "url": "https://mcp.atlassian.com/v2/mcp" } } }
     ```
     Si el agente tiene CLI de gestión de MCP, usa su comando de "añadir servidor" con transporte HTTP y nombre `atlassian`.
3. La puerta se considera abierta solo cuando una lectura real al tablero devuelve el issue. Sin puerta abierta, `slice-scaffolder` y `traceability-clerk` reportan el bloqueo y paran; `security-reviewer` puede revisar código, pero marca como gap cualquier nombre que no coincida con Jira.

## Invocación

- **Manual en desarrollo:** pídeselo al agente ("usa el security-reviewer sobre mi diff") o menciónalo con `@security-reviewer`.
- **En el CI** no corren agentes; corren los workflows reales:
  - Back: `sprint5-tests.yml` (restore/build/test + cobertura sobre `DentalConnect.slnx`), `db-migrations.yml` y `database-migrations.yml` (apply / revert / re-apply de migraciones EF Core sobre PostgreSQL), `codeql.yml`, `deploy-aws.yml` (incluye `docker build --tag dentalconnect-api:ci .`).
  - Front: `ci.yml` (`npm run lint`, `npm test`, `npm run build`), `codeql.yml`, `deploy-s3-cloudfront.yml` / `deploy-amplify.yml`.

## Hook de solo lectura

`readonly-git-guard.ps1` recibe por stdin el JSON de `PreToolUse`. Permite solo `git diff`, `git log` y `git show` sin encadenar (`;`, `&&`, `|`), sin redirigir (`>`, `<`), sin sustitución (`$(…)`) y sin opciones que escriban o ejecuten (`--output`, `--ext-diff`, `--textconv`, `-c`). Cualquier otro comando sale con exit 2 y el agente que lo ejecuta bloquea la llamada.

- Requisito: los hooks definidos en el frontmatter de un agente de proyecto solo se ejecutan **después de aceptar el diálogo de confianza del workspace** en el agente que los soporte.
- Usa `shell: powershell` y `powershell -ExecutionPolicy Bypass -File`, así que funciona con la política de ejecución por defecto de Windows.
- Al modificarlo, self-test en `powershell` 5.1 y `pwsh` 7 con payloads de ejemplo (comandos git permitidos y bloqueados) antes de fiarse del gate (skill `powershell-quirks`).
- Portabilidad: un agente que no soporte hooks ignora el campo `hooks` del frontmatter sin error; en esos agentes la solo lectura debe imponerse con sus propios permisos (ejemplo: `.opencode/agents/`).

## OpenCode

`.opencode/agents/` tiene los mismos tres agentes para OpenCode. Su cuerpo solo remite a `.agents/agents/<nombre>.md`, que sigue siendo la fuente única. En OpenCode no corren los hooks del frontmatter: la solo lectura de `security-reviewer` y `traceability-clerk` la imponen sus `permission` (`edit: deny` y bash solo para `git diff`, `git log` y `git show`). Si cambias la descripción de un agente aquí, cámbiala también allá.
