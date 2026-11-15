# TEC-07 · Eventos notificaciones y vencimientos

| Tarea | Jira | Sprint | Módulo | Contraparte | Estado |
|---|---|---|---|---|---|
| TEC-07 Eventos notificaciones y vencimientos | [DENT-31](https://uttt-proyecto.atlassian.net/browse/DENT-31) | DentalConnect Sprint 07 | `DentalConnect/ProposalExpirationWorker.cs`, `DentalConnect.Infrastructure/Services/ProposalExpirationProcessor.cs` | HU-13/DENT-13, HU-14/DENT-14, HU-15/DENT-15 | 🚧 Borrador |

## Contexto

**Actor:** servicio de fondo de la API; los avisos se destinan al consultorio dueño de la cita.
**Problema:** una propuesta vence según `ProposalExpiresAt`, pero hasta ahora solo se revierte al estado pendiente cuando el paciente intenta responder. No hay barrido proactivo que avise al consultorio.
**Depende de:** TEC-04 (persistencia de eventos), HU-13/DENT-13 (propuestas), HU-14/DENT-14 (regla de vencimiento) y HU-15/DENT-15 (bandeja de notificaciones).
**Nombre y criterio:** summary, criterio de finalización y responsable de DENT-31 leídos por Atlassian MCP el 15-Nov-2026. Responsable: Emanuel Cruz.

## Requisitos

- **R1.** El proceso periódico identifica citas `PROPOSED` cuyo `ProposalExpiresAt <= now` y vuelve a `PENDING`, limpiando los tres campos de propuesta.
- **R2.** El cambio de estado, el evento persistente `APPOINTMENT_ALTERNATIVE_EXPIRED` y la notificación in-app al consultorio se confirman en una única transacción.
- **R3.** Repetir una ejecución o correr más de una instancia no duplica ni la transición ni el aviso; un nuevo ciclo de propuesta puede reactivar los registros únicos existentes.
- **R4.** Una propuesta posterior a la hora actual no cambia; fallos al procesar una cita no detienen el barrido de las demás.
- **R5.** El servicio respeta cancelación del host y usa dependencias scoped sin capturar `DbContext` en el singleton.

## Diseño

- `DentalConnect.Application/Contracts/IProposalExpirationProcessor.cs`: contrato scoped para procesar una pasada con hora explícita y límite de lote.
- `DentalConnect.Infrastructure/Services/ProposalExpirationProcessor.cs`: candidatos vencidos, actualización condicional atómica y escritura idempotente de evento y notificación.
- `DentalConnect/ProposalExpirationWorker.cs`: `BackgroundService` que crea scope por pasada, registra errores y reintenta en la siguiente pasada.
- `DentalConnect/Program.cs`: registros DI; no agrega migración, porque `Appointment.ProposalExpiresAt`, `ClinicNotificationEvent` y `UserNotification` ya existen.
- Idempotencia: actualización condicional sobre `Status == PROPOSED && ProposalExpiresAt <= now`; solo quien actualiza una fila crea o reactiva evento y notificación dentro de la misma transacción. La restricción única existente protege contra duplicados concurrentes.

## Contratos

No agrega endpoints. Cuando una propuesta vence, `GET /api/appointments/mine` refleja `PENDING` y `proposalExpiresAt: null`; la clínica recibe un aviso de tipo `APPOINTMENT_ALTERNATIVE_EXPIRED` en `/api/notifications`.

## Pruebas

- `DentalConnect.Tests/ProposalExpirationProcessorTests.cs`: vencida, no vencida, igualdad de límite, repetición idempotente, reactivación en un ciclo posterior y error aislado por fila.
- Integración sobre PostgreSQL con la fixture `TEST_DATABASE_URL`: transacción conjunta, restricciones únicas y dos procesadores concurrentes.
- Comandos de cierre: `dotnet build DentalConnect.slnx -c Release` y `dotnet test DentalConnect.slnx -c Release` (con PostgreSQL para la suite de integración).

## Límites

- La notificación es in-app; email, SMS y push no son parte de DENT-31.
- La visualización completa al usuario corresponde a HU-15/DENT-15.
- El barrido corre dentro del host de API; si la operación crece o requiere garantías fuera del ciclo de vida del host, se deberá extraer a un worker durable separado.

## Aprobación

🚧 Borrador. No se marca aprobado hasta revisión de una persona distinta de Emanuel.
