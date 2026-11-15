---
name: traceability
description: Mantener la trazabilidad de DentalConnect — nombres de tareas delegados al tablero de Jira (Atlassian MCP, clave DENT-NN) y enlazados con spec.md ↔ código ↔ test en 3 capas, con estados honestos (Borrador con evidencia vs Aprobado). Úsala en cada HU, PR y cierre de spec.
---

## Propósito
Que cada requisito se pueda seguir del issue de Jira a la spec, al código y a la prueba; que ningún pendiente quede sin dueño; y que **ningún nombre se invente**: rama, spec, commits y PR salen del summary de `DENT-NN` leído por Atlassian MCP.

## Cuándo usarla
Al abrir una rama, al diferir alcance, al cerrar una spec y antes de cada PR.

## Precondiciones
- MCP de Atlassian **conectado** (puerta del proyecto, ver `.agents/agents/README.md`): sin él, no se nombra nada ni se reporta trazabilidad.
- Issue `DENT-NN` de la HU existente en `https://uttt-proyecto.atlassian.net` (proyecto `DentalConnect`).

## Pasos
1. **Leer el issue:** obtener `DENT-NN`, summary y criterios de aceptación por MCP. De ahí salen el título de la spec y el slug `specs/hu-NN-<slug-de-jira>/`.
2. **Rama:** la convención vigente del equipo es `Sprint-NN-<integrante>`; la HU se referencia en commits, no en el nombre de rama. Si la rama nueva nace por HU, incluir la clave: `feature/DENT-NN-<slug>`.
3. **Commits:** `<integrante o Sprint N>: <resumen con el nombre de Jira> (HU-NN · DENT-NN)`; **título de PR:** igual, con conteos de pruebas **reales** (`(Unit N · Api N · Postgres N)` o `(vitest N)`).
4. **Pruebas que citan su requisito:** el nombre o un comentario referencia la HU/spec (`// DENT-13 R1` o `Hu13_R2_...`).
5. **Pendientes en 3 capas:** `NOTE (DENT-NN)` en el código + `Límites` de `spec.md` + `Pendiente → DENT-NN` en `task.md`; el issue dueño debe existir en Jira.
6. **Estado de la spec (cabecera `Estado`):** "🚧 Borrador" al escribir; "🚧 Borrador con evidencia" al cerrar con pruebas en verde citadas; "✅ Aprobado" **solo** si el usuario indica el revisor (1 integrante distinto del autor) y el PR está en verde.
7. **HU de dos repos:** un PR por repo (`DentalConnect-back` y `DentalConnect-front`) con la misma clave `DENT-NN`; el PR del front enlaza al del back y la columna `Contraparte` de la spec los referencia.
8. **Fin de HU:** actualizar el issue en el tablero por MCP (estado y enlace al PR); Jira es la fuente del estado de gestión, la cabecera de la spec solo registra el lado técnico.

## Checklist
Cada requisito con ≥1 prueba · cada prueba con su spec · cada `NOTE (DENT-NN)` con dueño en las 3 capas · cabecera de spec completa con su clave Jira · nombres tomados del tablero vía MCP.

## Criterios de done
El agente `traceability-clerk` reporta `OK`; el PR referencia `DENT-NN` y su título coincide con el summary del tablero.

## Límites/trampas
- Nada de pruebas "sueltas" sin spec.
- No marcar "Aprobado" para "cerrar": es una mentira que el clerk reporta como gap.
- Sin MCP conectado está prohibido renombrar, crear spec o titular PRs "de memoria".
- El repo ya tenía ramas/commits sin clave Jira: no reescribir historia; la regla aplica desde la HU actual.

## Referencias
`specs/hu-13-proponer-horario-alternativo/` (ejemplo vivo de la cabecera), `.agents/agents/README.md` (instalación del MCP), `docs/calidad/`, agentes `traceability-clerk` y `security-reviewer`.
