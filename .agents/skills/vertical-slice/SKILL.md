---
name: vertical-slice
description: Entregar una HU de DentalConnect como un slice vertical completo en el repo activo — back .NET 10 (entidad, DTO/reglas, EF, endpoint, registro, pruebas en DentalConnect/) o front React/Vite (tipos, servicio, componente, rutas, vitest). Úsala al implementar cualquier endpoint o pantalla nueva.
---

## Propósito
Que cada HU se entregue completa y conectada en un solo PR, sin PRs por capa. El nombre de la HU y de sus artefactos viene del issue `DENT-NN` leído por **Atlassian MCP** (puerta obligatoria, ver `.agents/agents/README.md`).

## Cuándo usarla
En cada HU que agregue o cambie endpoints (back) o pantallas/consumo de API (front), y al revisar si un PR está completo.

## Precondiciones
- MCP de Atlassian conectado y el issue `DENT-NN` leído (nombre y criterios de aceptación).
- Spec de la HU en Borrador en `specs/hu-NN-<slug>/` con sus Contratos (skill `spec-first-writing`).
- En el back, el módulo dueño entre `DentalConnect/*Endpoints.cs`.

## Pasos — DentalConnect-back (.NET 10)
1. **Dominio** en `DentalConnect.Domain/Entities/`: entidad o enums de estado (UPPER_SNAKE) y reglas puras; sin EF ni ASP.NET dentro.
2. **Aplicación** en `DentalConnect.Application/Contracts/`: records de request/response e interfaz del servicio; las respuestas nunca exponen la entidad ni hashes, tokens ni PII de terceros (OWASP API3).
3. **Reglas de frontera** en el servicio/validadores: longitudes, rangos, estados válidos; la propiedad del recurso se verifica con aislamiento por consultorio (otro consultorio → 404 sin datos).
4. **Persistencia** en `DentalConnect.Infrastructure/`: `DbSet` + mapeo en `DentalConnectDbContext`; cambio de esquema ⇒ migración EF Core (`dotnet ef migrations add <Nombre> --project DentalConnect.Infrastructure --startup-project DentalConnect`) y fila en `docs/base-datos/migraciones.md`.
5. **Endpoint** en `DentalConnect/<Recurso>Endpoints.cs`: `MapGroup`/verbos con `.RequireAuthorization()` explícito (con rol si aplica), sin `try/catch` que engulla; códigos correctos (201 + `Results.Created`, 204, 400, 401, 403, 404, 409 con cuerpo UPPER_SNAKE).
6. **Registro** de DI/autenticación en `DentalConnect/Program.cs`, sin reordenar el pipeline.
7. **Pruebas** en `DentalConnect.Tests/`: reglas puras (xUnit) + `ApiTests` con `WebApplicationFactory` (flujo feliz y errores, 401/403 por endpoint) + prueba de concurrencia sobre PostgreSQL cuando toca agenda (skill `testing-strategy`).
8. **Conciliar** `spec/plan/task` y, si el front consume la ruta, dejar el contrato camelCase explícito en la spec para la contraparte.

## Pasos — DentalConnect-front (React 19 + Vite)
1. **Tipos** en `src/types/` copiando la forma camelCase real del endpoint (leer la respuesta o el DTO, no adivinar).
2. **Servicio** en `src/services/` reutilizando `api.ts`; auth por `AuthContext`/`cognito.ts`, sin reimplementar.
3. **UI**: componente en `src/components/` o página en `src/pages/`, montada en `src/routes/App.tsx` con guard de autenticación/rol si la pantalla es privada.
4. **Pruebas** vitest + @testing-library colocadas (`*.test.ts(x)`); cubren carga, error y el caso feliz del contrato.
5. **Formato/lint**: `npm run format` y `npm run lint` limpios.

## Checklist
Los componentes del repo activo presentes y conectados + pruebas de la HU con sus requisitos citados + tipos/contrato coincidentes entre los dos repos si hay consumo nuevo.

## Criterios de done
- Back: `dotnet build DentalConnect.slnx -c Release` 0/0 y `dotnet test DentalConnect.slnx -c Release` en verde.
- Front: `npm run build`, `npm run lint` y `npm run test` en verde.
- `security-reviewer` PASS y `traceability-clerk` OK sobre el PR.

## Límites/trampas
- No dividir el slice en PRs por capa; no commitear la Fase A (esqueleto) sin su Fase B.
- No tocar tablas/entidades de otro módulo: usar su interfaz pública.
- Un `catch` que engulla en un handler es bloqueante: el error se propaga al pipeline y sale con su código de contrato.
- Nombres (rama, spec, PR, commits) solo desde Jira vía MCP; nunca inventados.

## Referencias
Agentes `slice-scaffolder`, `security-reviewer`, `traceability-clerk`; skills `deferred-scope-fakes`, `drift-guards`, `testing-strategy`, `spec-first-writing`, `traceability`.
