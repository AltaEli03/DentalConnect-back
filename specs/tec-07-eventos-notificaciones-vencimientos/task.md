# Tareas · TEC-07 Eventos notificaciones y vencimientos

**Spec:** `spec.md`
**Plan:** `plan.md`
**Rama:** `feature/DENT-31-eventos-notificaciones-vencimientos`.
**Jira:** [DENT-31](https://uttt-proyecto.atlassian.net/browse/DENT-31) · Sprint `DentalConnect Sprint 07`.
**Responsable Jira:** Emanuel Cruz.

## Checklist

- [x] **T0. Fuente y línea base.** Leer DENT-31 por Atlassian MCP y comprobar las entidades/tablas existentes.
- [x] **T1. Diseño trazable.** Spec, plan y checklist registrados con summary, sprint, responsable, contrato, límites y pruebas derivados de DENT-31.
- [x] **T2. Procesador idempotente.** Transición condicional y persistencia del evento/aviso en la misma transacción; reutiliza los índices únicos existentes.
- [x] **T3. Worker alojado.** `BackgroundService` con scope por pasada, cancelación y aislamiento de errores por cita.
- [ ] **T4. Pruebas.** Cubrir umbral, futuro, repetición y concurrencia usando xUnit/PostgreSQL.
- [ ] **T5. Release y trazabilidad.** Build/tests, análisis estático, verificación de que no hay migración pendiente, PR hacia `develop`, y actualización Jira con SHA/CI reales.

## Pendientes con dueño

- Integración visual de avisos al paciente → HU-15/DENT-15.
- Pruebas transversales de permisos y accesibilidad → TEC-09/DENT-33.

## Evidencia actual

- `dotnet build DentalConnect.slnx -c Release --no-restore` → 0 errores, 0 advertencias.
- `dotnet test DentalConnect.slnx -c Release --no-build` → 38 aprobadas, 57 omitidas porque esta sesión no define `TEST_DATABASE_URL`.
- Las cuatro pruebas PostgreSQL de este cambio están añadidas y el workflow `sprint5-tests.yml` proporciona PostgreSQL 16; falta leer el resultado de CI antes de cerrar DENT-31 o cambiar su estado.
- No se añadió migración: los campos y tablas requeridos ya existen.
