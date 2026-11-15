---
name: drift-guards
description: Patrón anti-drift de DentalConnect — documento canónico + verificación que falla en ambos sentidos + job de CI, aplicado a migraciones EF Core, contratos back↔front y catálogos de rutas. Úsala al crear un documento que refleja el código o al agregar un guard nuevo.
---

## Propósito
Que los documentos de `docs/` y los contratos entre `DentalConnect-back` y `DentalConnect-front` no se pudran: si el código y el documento divergen, el CI falla.

## Cuándo usarla
Al agregar o cambiar endpoints o formas JSON; al crear un catálogo nuevo; al escribir un extractor o un job de verificación.

## Precondiciones
Si el guard es un script `.ps1`, seguir skill `powershell-quirks` (PS 5.1 y pwsh). Si es un job de CI, shell por defecto del workflow.

## Pasos
1. **Fuente única** declarada: qué documento o tipo canónico representa al código (hoy: `docs/base-datos/migraciones.md` para el esquema; las specs para los contratos; los endpoints en `DentalConnect/*Endpoints.cs` como dueños de las rutas).
2. **Verificación sin dependencias:** extractor (regex sobre `[MapGet/MapPost…]` en `*Endpoints.cs`, o `dotnet ef migrations list`) o prueba que compara código contra documento; normalizar antes de comparar (rutas, camelCase).
3. **Comparación en ambos sentidos:** elemento del código sin fila en el documento → FAIL; fila sin elemento en el código (huérfana) → FAIL.
4. **Self-test:** el primer run debe cazar un gap real o provocado (fila faltante + fila huérfana).
5. **Job de CI** paralelo, sin `needs`, exit 0/1 (precedentes vivos: `db-migrations.yml` y `database-migrations.yml` hacen apply → revert → re-apply sobre PostgreSQL 16; `ci.yml` del front corre lint/test/build).
6. Registrar el guard en esta skill y en `docs/calidad/`.

## Guards vigentes
- Migraciones EF Core ↔ PostgreSQL: `db-migrations.yml` / `database-migrations.yml` (ciclo apply/revert/re-apply con `dotnet-ef` 10) + fila en `docs/base-datos/migraciones.md`.
- Contrato back↔front: los tipos de `src/types/` del front se prueban contra la forma real de la respuesta en `*.test.ts(x)`; cualquier cambio de forma exige fixture/prueba en el mismo PR (patrón, no script: lo hace cumplir `security-reviewer` check 17).
- CodeQL en ambos repos como guard estático.
- No existe `docs/endpoints.md` ni `scripts/check_endpoints.ps1`: si se crea el catálogo de rutas, su extractor sigue los Pasos 1–5.

## Checklist
Documento canónico declarado · verificación verde en local y CI · job en el CI · specs que enlazan sin duplicar.

## Criterios de done
El job del guard falla en ambos sentidos cuando se le inyecta un gap y pasa con el repo real.

## Límites/trampas
- Un guard que solo compara en una dirección pierde filas huérfanas.
- Las rutas/flags solo de Development (`/openapi`, swagger) no son filas del catálogo: van en la sección de convenciones.
- No normalizar dentro de `{placeholders}` (`{appointmentId}`).

## Referencias
`.github/workflows/db-migrations.yml`, `docs/base-datos/migraciones.md`, `DentalConnect/*Endpoints.cs`, front `src/types/` y `src/services/`.
