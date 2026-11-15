# Sub-agentes de DentalConnect (convención híbrida agentes → skills)

Cobertura de los dos repos del proyecto:

- `DentalConnect-back`: API .NET 10 (`net10.0`): Minimal APIs (`DentalConnect/*Endpoints.cs`), Clean Architecture (`DentalConnect.Domain` → `DentalConnect.Application` → `DentalConnect.Infrastructure` → `DentalConnect`), EF Core + PostgreSQL 16, JWT con Cognito, xUnit 2.9 en `DentalConnect.Tests`.
- `DentalConnect-front`: React 19 + Vite + TypeScript: `src/auth` (Cognito), `src/pages`, `src/components`, `src/services`, `src/routes`, vitest + testing-library.

## Convención

- Los agentes definen **rol, permisos y criterio de done**, y actúan distinto según el repo donde se trabaje (back o front). El repo se detecta así: `DentalConnect.slnx` en la raíz → back; `package.json` con Vite y `src/` → front.
- Las reglas técnicas viven en `.agents/skills/<nombre>/SKILL.md`; los agentes **solo las enlazan, nunca las copian**. Si una skill cambia, el agente no se toca.

| Agente | Rol | Permisos | Cómo se imponen | Sin MCP de Atlassian |
|---|---|---|---|---|
| `slice-scaffolder` | Fase A: esqueleto compilable de una HU en el repo activo (back: entidad → DTO/reglas → EF → endpoint → pruebas; front: tipos → servicio → componente → prueba) | Lectura, edición y bash | `tools` en el frontmatter | **Se detiene** (sin nombre real todo nace con drift) |
| `security-reviewer` | Gate pre-push: critica el diff contra `origin/main` (OWASP API Security Top 10 2023 + checks de back o front) | Solo lectura + `git diff/log/show` | `disallowedTools: Write, Edit, NotebookEdit` + hook `PreToolUse` + modo seguro | Sigue con los checks de código y reporta `GAP-0` |
| `traceability-clerk` | Verifica Jira ↔ spec ↔ código ↔ test y reporta drift | Solo lectura + `git diff/log/show` | Igual que `security-reviewer` | Ejecuta el Grupo B y marca el Grupo A como no verificable |

## Vocabulario de los reportes

Todos los agentes usan los mismos términos para que un PR se lea igual venga de quien venga:

| Término | Significado |
|---|---|
| `GAP-0` | El MCP de Atlassian no está conectado o falló la autenticación. |
| `GAP-FUENTE` | Una fuente que el agente debía leer (spec, doc, skill, archivo) no existe o no se pudo leer. El agente nunca asume su contenido. |
| `FAIL` / `BLOQUEANTE` | Incumplimiento que debe corregirse antes del push o PR. |
| `WARN` / `AVISO` | Riesgo o deuda que no bloquea. |
| `NO VERIFICABLE` | El agente no pudo comprobarlo (con el motivo: MCP desconectado, fuente ausente, comando no permitido). No equivale a OK. |

Formato de salida: la **primera línea** es siempre el resumen (`RESULTADO: ... N FAIL, N WARN, N NO VERIFICABLE`), seguida de la tabla de hallazgos solo si los hay.

## Puerta obligatoria: Jira vía Atlassian MCP (nombres de tareas)

**Antes de cualquier acción relacionada con el proyecto** (crear o renombrar una spec, abrir rama, implementar una HU, revisar un diff, cerrar un PR), el nombre de la tarea se **delega al tablero de Jira**: cada HU es un issue `DENT-NN` en `https://uttt-proyecto.atlassian.net` y su *summary* es la única fuente del nombre. Nunca se inventa ni se copia de memoria.

1. Leer el issue con las herramientas del MCP de Atlassian (buscar por clave `DENT-NN`) y usar su summary en la cabecera de la spec, el slug de la carpeta, el título del PR y los commits.
2. Verificación rápida de la puerta: pide al MCP el issue `DENT-13`. Si la herramienta Atlassian no aparece o falla la autenticación, hay que ayudar al usuario a conectar el MCP según su agente:
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
3. La puerta se considera abierta solo cuando una lectura real al tablero devuelve el issue.
4. Qué pasa con la puerta cerrada (`GAP-0`):
   - `slice-scaffolder`: reporta el bloqueo y para, sin generar nada.
   - `security-reviewer`: revisa el código y marca como gap cualquier nombre que no pueda contrastar con Jira.
   - `traceability-clerk`: ejecuta los reportes que solo necesitan el repo (Grupo B) y marca como `NO VERIFICABLE` los que necesitan el tablero (Grupo A).

## Reportes de `traceability-clerk`

| Grupo | Reportes | Necesita Jira |
|---|---|---|
| A | Jira ↔ nombre, cabecera ↔ tablero, existencia del issue dueño de cada `NOTE` | Sí |
| B | HU ↔ spec, spec ↔ código ↔ test, `NOTE` en 3 capas, estados honestos, contraparte back ↔ front, migraciones | No |

## Invocación

- **Manual en desarrollo:** pídeselo al agente ("usa el security-reviewer sobre mi diff") o menciónalo con `@security-reviewer`.
- **En el CI** no corren agentes; corren los workflows reales:
  - Back: `sprint5-tests.yml` (restore/build/test + cobertura sobre `DentalConnect.slnx`), `db-migrations.yml` y `database-migrations.yml` (apply / revert / re-apply de migraciones EF Core sobre PostgreSQL), `codeql.yml`, `deploy-aws.yml` (incluye `docker build --tag dentalconnect-api:ci .`).
  - Front: `ci.yml` (`npm run lint`, `npm test`, `npm run build`), `codeql.yml`, `deploy-s3-cloudfront.yml` / `deploy-amplify.yml`.

## Solo lectura: tres capas de defensa

La solo lectura de `security-reviewer` y `traceability-clerk` no depende de una sola cosa:

1. **`disallowedTools: Write, Edit, NotebookEdit`** en el frontmatter: quita las herramientas de escritura.
2. **Hook `PreToolUse`** (`readonly-git-guard.ps1`): bloquea cualquier comando de shell que no sea `git diff`, `git log` o `git show`.
3. **Modo seguro en el propio agente:** cada agente tiene la instrucción de no ejecutar nada fuera de esos tres comandos aunque el hook no corra.

### Qué pasa cuando el hook no corre

El hook solo se ejecuta **después de aceptar el diálogo de confianza del workspace**, y un agente que no soporte hooks ignora el campo `hooks` sin error. En ambos casos solo quedan las capas 1 y 3. Eso significa:

- La capa 3 es una instrucción, no un mecanismo: protege contra errores, no contra un agente que la ignore.
- Para una garantía real, impón la solo lectura con los permisos del propio agente (ver sección OpenCode: `edit: deny` y bash limitado a `git diff|log|show`).
- Revisa el resultado de un agente de solo lectura como lo harías con cualquier otro: ante la duda, `git status` antes de confiar.

### El hook

`readonly-git-guard.ps1` recibe por stdin el JSON de `PreToolUse`. Permite solo `git diff`, `git log` y `git show` sin encadenar (`;`, `&&`, `|`), sin redirigir (`>`, `<`), sin sustitución (`$(…)`) y sin opciones que escriban o ejecuten (`--output`, `--ext-diff`, `--textconv`, `-c`). Cualquier otro comando sale con exit 2 y el agente bloquea la llamada.

- Usa `shell: powershell` y `powershell -ExecutionPolicy Bypass -File`, así que funciona con la política de ejecución por defecto de Windows.
- Al modificarlo, corre el self-test en `powershell` 5.1 y `pwsh` 7 antes de fiarte del gate (skill `powershell-quirks`).

### Self-test del hook

Los casos viven junto al script (`.agents/hooks/readonly-git-guard.cases.json`, a crear) como lista de payloads con su resultado esperado. Mínimo a cubrir:

| Tipo | Ejemplos |
|---|---|
| Permitidos | `git diff`, `git diff origin/main...HEAD`, `git log origin/main..HEAD --oneline`, `git show HEAD` |
| Bloqueados | `git push`, `git commit`, `git diff; rm x`, `git diff && ls`, `git diff \| sort`, `git diff > out.txt`, `git show $(whoami)`, `git diff --output=x`, `git -c core.pager=x log`, `git diff --ext-diff` |
| Ignorados | Payload de una herramienta que no es shell (p. ej. `Read`) |

Registro de la última verificación (actualiza al tocar el script):

| Fecha | Shell | Permitidos | Bloqueados | Ignorados | Resultado |
|---|---|---|---|---|---|
| `<fecha>` | powershell 5.1 | `<n>` | `<n>` | `<n>` | `<resultado>` |
| `<fecha>` | pwsh 7 | `<n>` | `<n>` | `<n>` | `<resultado>` |

## OpenCode

`.opencode/agents/` tiene los mismos tres agentes para OpenCode. Su cuerpo solo remite a `.agents/agents/<nombre>.md`, que sigue siendo la fuente única. En OpenCode no corren los hooks del frontmatter: la solo lectura de `security-reviewer` y `traceability-clerk` la imponen sus `permission` (`edit: deny` y bash solo para `git diff`, `git log` y `git show`).

**Sincronización:** la descripción de cada agente existe en dos lugares (`.agents/agents/` y `.opencode/agents/`). Si cambias una, cambia la otra. Pendiente: un paso de CI que compare ambas descripciones y falle si difieren, para no tener el mismo drift que estos agentes buscan detectar.

## Archivos opcionales que los agentes aprovechan si existen

Ninguno es obligatorio; si no existen, los agentes siguen con lo que tienen.

| Archivo | Para qué | Quién lo usa |
|---|---|---|
| `scripts/critic-guardrails.ps1` | Versión determinista de los checks 1–2 (`TODO` y secretos). Corre en CI y el dev puede pegar su salida al agente | `security-reviewer` |
| `docs/endpoints.md` | Catálogo de auth y códigos por ruta; fuente de verdad que reduce falsos positivos en el check 3 | `security-reviewer`, `traceability-clerk` |
| `.agents/hooks/readonly-git-guard.cases.json` | Casos del self-test del hook | Quien modifique el hook |

## Cuando cambies algo aquí

- **Cambió una regla técnica:** edita la skill, no el agente.
- **Cambió el nombre de una fuente** (doc, carpeta, workflow): búscala en los tres agentes y en este README; las rutas están citadas literalmente.
- **Añadiste un check:** numéralo al final de su bloque, no renumeres los existentes (los reportes de PR anteriores los citan por número).