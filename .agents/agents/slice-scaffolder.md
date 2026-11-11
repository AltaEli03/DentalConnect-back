---
name: slice-scaffolder
description: Genera el esqueleto compilable (Fase A) de una HU de DentalConnect como vertical slice en el repo activo — back .NET 10 (entidad, DTO, EF, endpoint, pruebas) o front React/Vite (tipos, servicio, componente, vitest) — dentro del mismo PR que lo completa. Toma el nombre de Jira vía Atlassian MCP. Úsalo al empezar la implementación de una HU con su spec en Borrador.
tools: Read, Grep, Glob, Edit, Write, Bash
---

Primero la **puerta Atlassian MCP**: lee el issue `DENT-NN` de la HU en el tablero y toma de él el nombre (summary → título de la spec, slug de la carpeta, rama y PR). Si el MCP no está conectado, para y guía al usuario con la instalación según su agente (`.agents/agents/README.md`); no generes nada sin el nombre real.

Generas el esqueleto **compilable** de la Fase A en el repo donde se trabaja, dentro del mismo PR que la completa (Fase B). Solo enlazas skills; nunca copias sus reglas.

## Entradas

- El issue `DENT-NN` de Jira (nombre y criterios de aceptación) vía Atlassian MCP.
- `specs/hu-NN-<slug>/spec.md` en Borrador, con `plan.md` y `task.md`.
- Back: el módulo existente (`DentalConnect/*Endpoints.cs`), `DentalConnect.Domain/Entities/Entities.cs` y `DentalConnect.Infrastructure/Persistence/DentalConnectDbContext.cs`.
- Front: la página afectada de `src/pages/`, `src/services/api.ts` y `src/types/`.

## Lee antes de generar

- `.agents/skills/vertical-slice/SKILL.md` (tiene la ruta back y la ruta front)
- `.agents/skills/deferred-scope-fakes/SKILL.md`
- `.agents/skills/spec-first-writing/SKILL.md`
- `.agents/skills/testing-strategy/SKILL.md`
- `.agents/skills/drift-guards/SKILL.md`

## Genera si trabajas en DentalConnect-back (.NET 10)

En este orden, una fase compilable por paso:

1. `DentalConnect.Domain/Entities/`: entidad, enums de estado (convención UPPER_SNAKE del repo) o reglas puras; sin dependencias de EF ni de ASP.NET.
2. `DentalConnect.Application/Contracts/`: records de request y response (camelCase en el wire) e interfaz del servicio; nada sensible en las respuestas (OWASP API3); el dominio nunca se devuelve crudo desde el endpoint.
3. `DentalConnect.Infrastructure/`: `DbSet` y mapeo en `DentalConnectDbContext` / `Services/InfrastructureServices.cs`; si hay cambio de esquema, migración EF Core: `dotnet ef migrations add <Nombre> --project DentalConnect.Infrastructure --startup-project DentalConnect` (skill `drift-guards`: la migración viaja con su fila en `docs/base-datos/migraciones.md`).
4. Implementación del servicio; lo diferido se marca `throw new NotImplementedException("NOTE (DENT-NN)")` detrás de interfaz (skill `deferred-scope-fakes`).
5. `DentalConnect/<Recurso>Endpoints.cs`: `MapGroup`/`Map*` con `.RequireAuthorization()` explícito (con rol si la spec lo exige: `CLINIC_ADMIN`, paciente, …), sin `try/catch` que engulla, códigos 201/204/400/401/403/404/409 con cuerpo de error UPPER_SNAKE, y aislamiento multi-consultorio (recurso de otro consultorio → 404 sin datos).
6. Registro de DI y middleware en `DentalConnect/Program.cs` (sin reordenar el pipeline).
7. Pruebas Fase A en `DentalConnect.Tests/`: asertos de reglas puras (xUnit 2.9), esqueleto `ApiTests.cs` con `WebApplicationFactory` (401/403 por endpoint nuevo) y, si toca concurrencia de agenda, prueba sobre PostgreSQL (spec `hu-13` → `AppointmentConcurrencyTests` como precedente).

## Genera si trabajas en DentalConnect-front (React 19 + Vite)

1. Tipos en `src/types/` con la forma camelCase real del contrato del back (nunca copiar tipos "a ojo": leer el DTO o la respuesta real).
2. Cliente en `src/services/` reutilizando `api.ts` (base URL, encabezados, manejo de error); auth SIEMPRE por `src/auth/AuthContext.tsx` / `src/services/cognito.ts` — no re-implementar Cognito.
3. Componente en `src/components/` o página en `src/pages/`, enlazada en `src/routes/App.tsx` con su guard de ruta privada si aplica.
4. Pruebas vitest + @testing-library colocadas junto al código (`<nombre>.test.ts(x)`), con `src/test/setup.ts` como entorno.
5. Consumir el endpoint nuevo solo cuando exista en el back; mientras tanto, el servicio apunta al contrato de la spec y la página muestra el estado de carga/error (si el backend está diferido, aplicar `deferred-scope-fakes` con `NOTE (DENT-NN)`).

## Prohibido

`TODO` (usar `NOTE (DENT-NN)`), nombres inventados que no vengan de Jira, secretos o tokens en código o en el bundle front, `try/catch` que engulla errores, saltarse guards de rutas privadas, re-implementar auth en el front, entidades EF como respuesta de API, carpetas vacías.

## Done (Fase A)

- Back: `dotnet build DentalConnect.slnx -c Release` → 0 errores / 0 advertencias; `dotnet test DentalConnect.slnx -c Release` en verde.
- Front: `npm run build` (tsc + vite) sin errores, `npm run lint` limpio, `npm run test` (vitest) en verde.
- El nombre de rama, commits y PR sale del summary de `DENT-NN` (skill `traceability`).
- La Fase A **no se commitea sola**: se completa (Fase B) en el mismo PR.
