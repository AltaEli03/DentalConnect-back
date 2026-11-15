# Plan · TEC-07 Eventos notificaciones y vencimientos (DENT-31)

1. Confirmar el esquema existente y los helpers/eventos que ya generan HU-13 y HU-14; no introducir tablas ni paquetes nuevos.
2. Añadir el contrato de procesamiento scoped y su implementación en Infrastructure.
3. Usar una transacción por cita con una actualización condicional sobre estado y fecha. Encolar/reactivar `ClinicNotificationEvent` y `UserNotification` solo si la actualización afectó una fila.
4. Añadir un `BackgroundService` pequeño con scope por pasada, `PeriodicTimer`, manejo por cita y `CancellationToken`.
5. Registrar servicios en `Program.cs`; excluir el barrido automático de los modos `--seed` y `--migrate` porque esos modos salen antes de `app.Run()`.
6. Cubrir reglas, repetición y concurrencia con xUnit/PostgreSQL. No depender del reloj del sistema en los tests: pasar `now` explícito al processor.
7. Correr build/tests, ciclo de migraciones sin cambios, revisión de seguridad/trazabilidad, crear PR hacia `develop`; solo promover a `release` y `main` tras CI verde.

## Guardarraíles

- No enviar avisos de vencimiento a pacientes.
- No alterar citas `CONFIRMED`, `PENDING`, `REJECTED` o no vencidas.
- No depender de una consulta previa como mecanismo de exclusión: la mutación condicional es la fuente de exclusión concurrente.
- Una notificación/evento reusado debe actualizar fecha, desmarcar leído o reabrir `ProcessedAt` cuando corresponde a un ciclo nuevo.

## Validación

- Unit: bordes y cálculo de selección de candidatos, si el comportamiento puede extraerse como regla pura.
- PostgreSQL: fila vencida se revierte una sola vez; fila futura no cambia; evento y notificación existen; repetición no duplica; ejecuciones concurrentes producen una transición/aviso; un error no aborta el lote completo.
- Release: build y suite real; CodeQL/deploy por PR; sin migración estructural.
