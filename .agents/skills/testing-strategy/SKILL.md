---
name: testing-strategy
description: Qué suite de DentalConnect cubre qué (back: reglas puras, ApiTests con WebApplicationFactory, concurrencia sobre PostgreSQL; front: vitest + testing-library), comandos reales y reglas anti-flakiness con xUnit 2.9/VSTest y vitest. Úsala al planear las pruebas de una HU o al auditar cobertura.
---

## Propósito
Que cada tipo de riesgo tenga su prueba, con pruebas deterministas y conteos reales.

## Cuándo usarla
Al escribir la sección `Tests` de una spec, al crear pruebas y al auditar.

## Precondiciones
Build Release limpio: `dotnet build DentalConnect.slnx -c Release` (back) y `npm run build` (front).

## Pasos
1. **Matriz suite → responsabilidad:**

| Suite | Repo | Cubre | Herramientas |
|---|---|---|---|
| `DentalConnect.Tests/` (reglas) | back | Reglas puras de dominio y calendario: `BusinessRuleTests.cs`, `DentistScheduleRulesTests.cs`, `PublicAvailabilityRulesTests.cs` | xUnit 2.9 + VSTest + coverlet |
| `DentalConnect.Tests/` (API) | back | Flujos HTTP completos con `WebApplicationFactory` (`ApiTests.cs`): auth por rol (fake de JWT/Cognito), códigos 201/400/401/403/404/409 | `Microsoft.AspNetCore.Mvc.Testing` 10.x |
| `DentalConnect.Tests/` (concurrencia) | back | Doble reserva y contienda de agenda sobre PostgreSQL real (`AppointmentConcurrencyTests.cs`); el CI levanta Postgres y el local usa `docker-compose.yml` | EF Core + Npgsql 10 |
| `src/**/*.test.ts(x)` | front | Servicios (`clinicSearch.test.ts` como precedente), componentes y páginas con jsdom; contrato camelCase contra la forma real del back | vitest + @testing-library + `src/test/setup.ts` |

2. **Comandos:** back `dotnet test DentalConnect.slnx -c Release` (en el CI, con `--no-build` después del build Release; también se puede filtrar por proyecto de prueba); front `npm run test` (= `vitest run`).
3. **Anti-flakiness:** datos únicos por prueba (`Guid`); nada de conteos exactos en tablas compartidas; en pruebas de API del back, hosts con estado en memoria corren sin paralelismo por colección compartida (xUnit: `[Collection]` + fixture cuando comparten el host).
4. **Frontera exacta:** probar `>=`/`<`, los mensajes y ambas ramas.
5. **Grupo de control:** cuando una prueba asegura una ausencia (p. ej. sin detalle de error en Production), otra prueba confirma la presencia en el caso contrario (Development).
6. **Migraciones:** toda migración nueva debe sobrevivir el ciclo apply → revert a la anterior → re-apply que corren `db-migrations.yml` y `database-migrations.yml` (es su gate de DB).
7. **HU sensibles:** las HU que tocan citas/auth (HU-09 en adelante) llevan pruebas de reglas + API (401/403 por endpoint nuevo) obligatoriamente; si hay concurrencia de agenda, también la de Postgres.

## Checklist
Matriz respetada · prueba que cita su requisito (`// DENT-13 R1`) · conteos reales citados en la spec/PR · lint y test del front verdes si la HU toca el front.

## Criterios de done
Suites del repo activo en verde; conteos citados en la spec y en el PR; cobertura del back publicada por `sprint5-tests.yml` (XPlat Code Coverage + resumen).

## Límites/trampas
- El runner del back es xUnit **2.9 sobre VSTest** (`Microsoft.NET.Test.Sdk` 17.x): no asumir la sintaxis de Microsoft.Testing.Platform ni `TestContext.Current.CancellationToken` de xUnit v3.
- Cobertura del front: `vitest run` sin umbral fijo hoy; si se fija uno, medir antes (skill `empirical-verification`).
- Pruebas de API que dependen de CORS/HSTS reales: usar host que no sea localhost si se verifica HSTS (el middleware lo excluye).

## Referencias
`DentalConnect.Tests/*.cs`, `docs/calidad/`, workflows `sprint5-tests.yml`, `db-migrations.yml`; front `vitest.config.ts`, `src/test/setup.ts`, `src/services/clinicSearch.test.ts`.
