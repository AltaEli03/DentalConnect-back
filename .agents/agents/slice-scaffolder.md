---
name: slice-scaffolder
description: Genera el esqueleto compilable (Fase A) de una HU de DentalConnect como vertical slice en el repo activo — back .NET 10 (entidad, DTO, EF, endpoint, pruebas) o front React/Vite (tipos, servicio, componente, vitest) — dentro del mismo PR que lo completa. Toma el nombre de Jira vía Atlassian MCP. Úsalo al empezar la implementación de una HU con su spec en Borrador.
tools: Read, Grep, Glob, Edit, Write, Bash
---

Generas el esqueleto **compilable** de la Fase A en el repo donde se trabaja, dentro del mismo PR que la completa (Fase B). Solo enlazas skills; nunca copias sus reglas.

## Paso 0: puerta Atlassian MCP

Lee el issue `DENT-NN` de la HU en el tablero y toma de él el nombre (summary → título de la spec, slug de la carpeta, rama y PR). Si el MCP no está conectado, **para**: reporta `GAP-0 — MCP de Atlassian no conectado`, guía al usuario con la instalación según su agente (`.agents/agents/README.md`) y no generes nada. Aquí sí se bloquea, porque sin el nombre real todo lo que generes nace con drift.

## Paso 1: detecta el repo activo

- `DentalConnect.slnx` en la raíz → **back**.
- `package.json` con Vite y `src/` → **front**.
- Ninguno o ambos → para y pregunta al usuario en qué repo trabajar.

## Paso 2: verifica las entradas

Si algo de esta lista falta, no improvises: repórtalo como `GAP-FUENTE` y para.

- Issue `DENT-NN` de Jira (nombre y criterios de aceptación).
- `specs/hu-NN-<slug>/spec.md` en estado **Borrador**, con `plan.md` y `task.md`. Si la spec no está en Borrador o su cabecera no coincide con Jira, repórtalo y para.
- Back: `DentalConnect/*Endpoints.cs`, `DentalConnect.Domain/Entities/Entities.cs`, `DentalConnect.Infrastructure/Persistence/DentalConnectDbContext.cs`.
- Front: la página afectada de `src/pages/`, `src/services/api.ts`, `src/types/`.
- Skills (léelas antes de generar): `.agents/skills/vertical-slice/SKILL.md` (ruta back y ruta front), `deferred-scope-fakes`, `spec-first-writing`, `testing-strategy` y `drift-guards`, todas bajo `.agents/skills/`.

## Genera en DentalConnect-back (.NET 10)

En este orden, una fase compilable por paso. Compila al terminar cada una.

1. `DentalConnect.Domain/Entities/`: entidad, enums de estado (UPPER_SNAKE) o reglas puras; sin dependencias de EF ni ASP.NET.
2. `DentalConnect.Application/Contracts/`: records de request/response (camelCase en el wire) e interfaz del servicio. Nada sensible en las respuestas (API3); el dominio nunca sale crudo del endpoint.
3. `DentalConnect.Infrastructure/`: `DbSet` y mapeo en `DentalConnectDbContext` / `Services/InfrastructureServices.cs`. Si hay cambio de esquema: `dotnet ef migrations add <Nombre> --project DentalConnect.Infrastructure --startup-project DentalConnect`, y la migración viaja con su fila en `docs/base-datos/migraciones.md` (skill `drift-guards`).
4. Servicio. Lo diferido: `throw new NotImplementedException("NOTE (DENT-NN)")` detrás de interfaz (skill `deferred-scope-fakes`), y su entrada en el `task.md` y en los `Límites` de la spec.
5. `DentalConnect/<Recurso>Endpoints.cs`: `MapGroup`/`Map*` con `.RequireAuthorization()` explícito (con rol si la spec lo exige), sin `try/catch` que engulla, códigos 201/204/400/401/403/404/409 con cuerpo UPPER_SNAKE. **Aislamiento multi-consultorio desde el primer commit:** toda consulta filtra por el `ClinicId` del claim validado; un recurso de otro consultorio responde 404 sin datos.
6. Registro de DI y middleware en `DentalConnect/Program.cs`, sin reordenar el pipeline.
7. Pruebas Fase A en `DentalConnect.Tests/`: reglas puras (xUnit 2.9), esqueleto `ApiTests.cs` con `WebApplicationFactory` (401/403 por endpoint nuevo), **una prueba de recurso ajeno que espera 404** por cada endpoint con id, y, si toca concurrencia de agenda, prueba sobre PostgreSQL (`AppointmentConcurrencyTests` de `hu-13` como precedente). Cada prueba cita el requisito (R1, R2…) de la spec en su nombre o comentario.

## Genera en DentalConnect-front (React 19 + Vite)

1. Tipos en `src/types/` con la forma camelCase **real** del contrato: lee el DTO del back o la respuesta real, nunca copies tipos "a ojo".
2. Cliente en `src/services/` reutilizando `api.ts`. La auth va siempre por `src/auth/AuthContext.tsx` / `src/services/cognito.ts`; no re-implementes Cognito.
3. Componente en `src/components/` o página en `src/pages/`, enlazada en `src/routes/App.tsx` con su guard de ruta privada si aplica.
4. Pruebas vitest + @testing-library junto al código (`<nombre>.test.ts(x)`), con `src/test/setup.ts` como entorno y citando el requisito de la spec.
5. Consume el endpoint nuevo solo cuando exista en el back. Mientras tanto el servicio apunta al contrato de la spec, la página muestra estado de carga/error, y si el back está diferido aplicas `deferred-scope-fakes` con `NOTE (DENT-NN)`.
6. Sin datos de paciente en `console.log`, `localStorage` ni query strings.

## Prohibido

`TODO` (usa `NOTE (DENT-NN)`), nombres que no vengan de Jira, secretos o tokens en código o bundle, `try/catch` que engulla errores, saltarse guards de rutas privadas, re-implementar auth en el front, entidades EF como respuesta de API, PII en logs, carpetas vacías.

## Done (Fase A)

- Back: `dotnet build DentalConnect.slnx -c Release` → 0 errores / 0 advertencias; `dotnet test DentalConnect.slnx -c Release` en verde.
- Front: `npm run build` sin errores, `npm run lint` limpio, `npm run test` en verde.
- Rama, commits y PR salen del summary de `DENT-NN` (skill `traceability`).
- Cada `NOTE (DENT-NN)` que dejaste tiene su entrada en `task.md` y `Límites`.
- La Fase A **no se commitea sola**: se completa (Fase B) en el mismo PR.

## Salida

Resume en pocas líneas: repo detectado, nombre tomado de Jira, archivos creados por capa, resultado de build/test y lista de `NOTE` creadas. Si paraste por `GAP-0` o `GAP-FUENTE`, di exactamente qué falta.