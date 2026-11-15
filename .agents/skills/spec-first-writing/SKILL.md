---
name: spec-first-writing
description: Redactar specs de DentalConnect (HU o cambios técnicos) con la plantilla de tres archivos (spec.md + plan.md + task.md) y criterios de aceptación verificables por comando o prueba; el nombre sale del tablero de Jira vía Atlassian MCP. Úsala antes de implementar cualquier HU o cambio técnico, y al refinar una spec.
---

## Propósito
Que cada cambio nazca de una spec vertical (`spec.md` + `plan.md` + `task.md`) con criterios que una persona o el CI puedan comprobar.

## Cuándo usarla
- Al iniciar el trabajo de una HU (crear `specs/hu-NN-<slug>/` en `DentalConnect-back`; las HU puramente de UI también pueden vivir en el front con la misma clave).
- Antes de un cambio técnico (migraciones, infraestructura, ambientes).
- Al refinar una spec existente o al cerrarla (conciliar con lo implementado).

## Precondiciones
- MCP de Atlassian conectado y el issue `DENT-NN` leído: de su summary salen el nombre de la spec y el slug de la carpeta (si el MCP falla, parar y guiar la instalación, ver `.agents/agents/README.md`).
- Conocer el repo activo (back o front) y el módulo dueño (`DentalConnect/*Endpoints.cs` o `src/pages/…`).

## Pasos
1. Cabecera de `spec.md` con la tabla del repo (`docs/` y `specs/hu-13-*` como referencia viva): `HU | Jira (DENT-NN con enlace) | Sprint | Módulo | Contraparte | Estado`, valores tomados del tablero.
2. **Contexto:** actor, problema y `**Depende de**: HU-NN/DENT-NN`.
3. **Requisitos:** numerados (R1, R2…), imperativos, referenciando los AC del issue de Jira cuando existan; los que solo están en Jira se citan con su clave.
4. **Diseño:** archivos reales del repo activo — back: rutas bajo `DentalConnect.Domain` / `DentalConnect.Application` / `DentalConnect.Infrastructure` / `DentalConnect/…Endpoints.cs`; front: `src/types`, `src/services`, `src/pages`. Si hay fake-first, declararlo con sus `NOTE (ID)` (skill `deferred-scope-fakes`).
5. **Contratos:** la forma JSON real de la ruta (verbos y rutas salen de los `*Endpoints.cs`; el cuerpo en camelCase que consume el front); indicar fixture de contrato en el front si cambia una forma ya consumida.
6. **Tests:** rutas reales de prueba — back: archivos de `DentalConnect.Tests/`; front: `*.test.ts(x)` junto al código (skill `testing-strategy`).
7. **Criterios:** cada uno con su comando o prueba y su umbral (`dotnet test DentalConnect.slnx -c Release` → N/N; `npm run test` → N/N; ciclo de migraciones en verde cuando toca esquema).
8. **Límites:** lo que no cubre + diferidos con su spec/issue dueño.
9. **Aprobación:** "🚧 Borrador" al escribir; "🚧 Borrador con evidencia" al cerrar; "✅ Aprobado" solo con revisor indicado por el usuario.
10. `plan.md` (componentes, decisiones, guardarraíles, validación, secuencia con los comandos del repo) y `task.md` (T1…Tn con verificación y pendientes `→ DENT-NN`).

## Checklist
- Cabecera con clave Jira leída por MCP · Requisitos con AC · Diseño con archivos reales del repo activo · Contratos con forma real · Tests con rutas · Criterios con comando · Límites con dueños · Aprobación honesta.

## Criterios de done
Ningún criterio ambiguo: cada uno se puede ejecutar o comprobar con los comandos del repo activo; la cabecera está completa.

## Límites/trampas
- No escribir specs horizontales por capa ("todos los DTOs"): una spec = una HU vertical.
- No duplicar el contrato en los dos repos de memoria: la spec del back fija la forma y el front la importa/enlaza (skill `drift-guards`).
- No inventar umbrales ni nombres: los nombres vienen de Jira, los umbrales se miden (skill `empirical-verification`).
- Una spec de front enlaza su contraparte del back (y viceversa) con la misma clave `DENT-NN` (skill `traceability`).

## Referencias
`specs/hu-13-proponer-horario-alternativo/{spec.md,plan.md,task.md}`, `DentalConnect-back/README.md`, front `docs/quality-and-environments.md`, skills `vertical-slice`, `traceability`, `deferred-scope-fakes`, `testing-strategy`.
