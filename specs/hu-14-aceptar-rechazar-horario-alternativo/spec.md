# HU-14 · Aceptar o rechazar un horario alternativo

| HU | Jira | Sprint | Módulo | Contraparte | Estado |
|---|---|---|---|---|---|
| HU-14 Aceptar o rechazar un horario alternativo | [DENT-14](https://uttt-proyecto.atlassian.net/browse/DENT-14) | DentalConnect Sprint 07 | `DentalConnect/AppointmentEndpoints.cs` (citas) | `DentalConnect-front` HU-14 (misma clave DENT-14; pendiente) | 🚧 Borrador |

## Contexto

**Actor:** paciente autenticado dueño de la solicitud (`Appointment.PatientSubject == sub`).
**Problema:** el consultorio propuso un horario alternativo (HU-13), pero el paciente no tiene forma de comparar ambos horarios ni de aceptar o rechazar la propuesta. Sin esa respuesta, la solicitud queda `PROPOSED` y el consultorio no sabe si reservar el horario.
**Depende de:** HU-03/DENT-3 (sesión autenticada), HU-13/DENT-13 (`PROPOSED`, campos de propuesta y `POST /propose`), TEC-06/DENT-30 (transacciones y exclusión de agenda), TEC-07/DENT-31 (vencimiento y aviso al consultorio), HU-15/DENT-15 (entrega de notificaciones).
**Dependencias Jira verificadas:** DENT-14 (HU-14), DENT-13 (HU-13, Terminada), DENT-3 (HU-03, Terminada), DENT-30 (TEC-06, Terminada), DENT-31 (TEC-07, Por refinar), DENT-15 (HU-15, Por refinar).
**Nombre y criterios:** summary y descripción de `DENT-14` leídos por Atlassian MCP el 11-Nov-2026.

## Requisitos

- **R1 (AC1).** Dado un `Appointment` en `PROPOSED` con propuesta vigente y el intervalo libre, cuando el paciente dueño acepta, entonces la cita pasa a `CONFIRMED` de forma atómica, `StartsAt/EndsAt` toman el horario propuesto, se limpian `ProposedStartsAt/ProposedEndsAt/ProposalExpiresAt` y se registra el evento `APPOINTMENT_ALTERNATIVE_ACCEPTED`. Responde `200` con la cita confirmada.
- **R2 (AC2).** Dado un `Appointment` en `PROPOSED` con propuesta vigente, cuando el paciente dueño rechaza, entonces la cita pasa a `REJECTED`, se registran `RejectedAt = now` y `RejectedBy = sub` del paciente, se conservan los campos de la propuesta para auditoría y se registra `APPOINTMENT_ALTERNATIVE_REJECTED`.
- **R3 (AC3).** Si al aceptar el intervalo propuesto ya está ocupado, fuera de jornada, en descanso, bloqueado o el paciente ya tiene otra cita `CONFIRMED` que se cruza, entonces responde `409 SLOT_UNAVAILABLE` o `409 PATIENT_CONFLICT`, no confirma y la cita sigue `PROPOSED`. Una carrera de exclusión (`23P01`), unicidad (`23505`) o serialización (`40001`) responde `409 APPOINTMENT_CONFLICT` sin confirmar.
- **R4 (AC4).** Si `ProposalExpiresAt <= now`, aceptar o rechazar responde `409 PROPOSAL_EXPIRED`; la cita vuelve a `PENDING`, se limpian los campos de propuesta y se registra `APPOINTMENT_ALTERNATIVE_EXPIRED`. El barrido proactivo y el aviso al consultorio son de TEC-07/DENT-31 (`NOTE (DENT-31)`).
- **R5.** `GET /api/appointments/{appointmentId}/proposal` devuelve al paciente dueño el estado actual, el horario original, el propuesto y `proposalExpiresAt` para comparar antes de responder. Otro paciente o un token de consultorio recibe `404` sin datos.
- **R6.** La autorización es por propiedad: sin token o sin claim `sub`, `401`; cita inexistente, de otro paciente o de otro consultorio, `404`. No se exige rol `PATIENT` porque Cognito no emite ese grupo en el piloto; por eso no hay `403` en estas rutas y los casos felices usan un token autenticado sin rol. No se exige `email_verified`: la cita solo pudo crearse con un token verificado y la frontera es la propiedad del recurso.
- **R7.** Aceptar revalida dentista activo y del mismo consultorio, servicio activo y ofrecido por consultorio y dentista, duración vigente, jornada, descanso, bloqueos y citas `CONFIRMED` del dentista. Rechazar no revalida disponibilidad.
- **R8.** El evento de aceptación, rechazo o vencimiento se guarda en la misma transacción que el cambio de estado. Es único por `(AppointmentId, EventType)`; si el vencimiento se repite tras una nueva propuesta, se reutiliza el evento existente. La visualización in-app del aviso es de HU-15/DENT-15 (`NOTE (DENT-15)`); email, SMS y push quedan fuera de alcance.
- **R9.** No hay cambio de esquema: se reutilizan `ProposedStartsAt`, `ProposedEndsAt`, `ProposalExpiresAt`, `RejectedAt`, `RejectedBy` y `ClinicNotificationEvent`. No se agrega migración ni paquete NuGet.
- **R10.** El contrato camelCase queda fijo para el front HU-14. La UI de paciente es un PR separado en `DentalConnect-front` con la misma clave `DENT-14`.

## Diseño

### Estado

```text
PENDING --HU-13 propone--> PROPOSED
PROPOSED --HU-14 acepta--> CONFIRMED
PROPOSED --HU-14 rechaza--> REJECTED
PROPOSED --HU-14 perezoso o TEC-07 vence--> PENDING
```

- `PROPOSED` no reserva el intervalo; solo `CONFIRMED` participa en las exclusiones de agenda.
- Al aceptar, el horario original se reemplaza por el propuesto. El horario original no se conserva después de la aceptación; si se requiere auditoría, va en una spec posterior.
- Al rechazar, `StartsAt/EndsAt` siguen siendo los originales y los campos propuestos se conservan para que el consultorio vea qué se rechazó.
- Al vencer, se limpian los campos propuestos y la solicitud vuelve a `PENDING`.

### Dominio

En `DentalConnect.Domain/Entities/AppointmentScheduling.cs`, agregar a `AppointmentProposalRules`:

```csharp
public static bool IsExpired(DateTimeOffset now, DateTimeOffset expiresAt) => expiresAt <= now;
```

Sin EF ni ASP.NET. `ExpiresAt` e `IsPast` de HU-13 no cambian.

### Persistencia

Sin migración. `Appointment` ya tiene las columnas necesarias y `CK_Appointment_Proposal` acepta tanto los tres campos nulos (aceptación o vencimiento) como los tres no nulos con `ProposedStartsAt < ProposedEndsAt` (rechazo). `ClinicNotificationEvent` ya tiene el índice único por cita y tipo. No se toca `DentalConnectDbContext` ni `docs/base-datos/migraciones.md`.

### Endpoint

En `DentalConnect/AppointmentEndpoints.cs`:

- `GET /api/appointments/{appointmentId}/proposal` → `GetProposalAsync`.
- `POST /api/appointments/{appointmentId}/proposal/accept` → `AcceptProposalAsync`.
- `POST /api/appointments/{appointmentId}/proposal/reject` → `RejectProposalAsync`.
- Las tres rutas usan `.RequireAuthorization()`.
- `AcceptProposalAsync` y `RejectProposalAsync` comparten `RespondToProposalAsync(appointmentId, accept, http, db, ct)` para no duplicar ownership, transacción, vencimiento y eventos.
- `TryGetPatientSubject` lee `sub` o `ClaimTypes.NameIdentifier`; si falta, `401`.
- La transacción es `Serializable`; los conflictos `40001`, `23505` y `23P01` se convierten en `409 APPOINTMENT_CONFLICT`.
- La respuesta reutiliza el helper `AppointmentResponse`; no se agregan proyecciones ni se modifican las del consultorio.
- El encolado de eventos nuevos lleva `NOTE (DENT-15)` para la visualización in-app de HU-15; el barrido de vencimiento lleva `NOTE (DENT-31)`.

### Autorización

No se agrega el rol `PATIENT`: el piloto no lo emite y HU-09 crea citas solo con `sub` verificado. La consulta filtra siempre por `Id == appointmentId && PatientSubject == sub`; un token de consultorio no coincide y recibe `404`. Esto evita IDOR (OWASP API1) y no expone PII de otros pacientes (OWASP API3). El `GET` devuelve solo IDs y horarios, no nombres ni contacto.

### Eventos

| Evento | Cuándo | Consumidor |
|---|---|---|
| `APPOINTMENT_ALTERNATIVE_ACCEPTED` | Aceptación exitosa | TEC-07 / HU-15 |
| `APPOINTMENT_ALTERNATIVE_REJECTED` | Rechazo exitoso | TEC-07 / HU-15 |
| `APPOINTMENT_ALTERNATIVE_EXPIRED` | Vencimiento detectado al responder o barrido de TEC-07 | TEC-07 / HU-15 |

`QueueClinicEventAsync` busca el evento tracked por cita y tipo; si existe, limpia `ProcessedAt` y actualiza `CreatedAt`; si no, lo agrega. Así el vencimiento perezoso y el barrido de TEC-07 no duplican avisos.

### Proyecciones y documentación

El listado y el detalle del consultorio ya exponen `proposedStartsAt`, `proposedEndsAt` y `proposalExpiresAt`; no cambian. El detalle del consultorio ya expone `rejection` cuando el estado es `REJECTED`, por lo que ve `rejectedAt/rejectedBy` del paciente sin cambios adicionales.

Al cerrar la HU se eliminó la nota de aceptación/rechazo de `ProposeAsync` y se actualizaron las referencias de HU-13; el `NOTE (DENT-15)` y el `NOTE (DENT-31)` siguen vigentes.

## Contratos

### `GET /api/appointments/{appointmentId}/proposal`

Sin body. Response `200`:

```json
{
  "data": {
    "id": "c...",
    "folio": "DC-...",
    "status": "PROPOSED",
    "clinicId": "c...",
    "dentistId": "c...",
    "dentalServiceId": "c...",
    "startsAt": "2026-11-15T17:00:00+00:00",
    "endsAt": "2026-11-15T17:30:00+00:00",
    "proposedStartsAt": "2026-11-12T18:00:00+00:00",
    "proposedEndsAt": "2026-11-12T18:30:00+00:00",
    "proposalExpiresAt": "2026-11-12T12:00:00+00:00",
    "timeZoneId": "America/Mexico_City"
  }
}
```

Si no hay propuesta activa, `proposedStartsAt`, `proposedEndsAt` y `proposalExpiresAt` son `null` y `status` refleja el estado real.

### `POST /api/appointments/{appointmentId}/proposal/accept`

Sin body. Response `200`:

```json
{
  "data": {
    "id": "c...",
    "folio": "DC-...",
    "status": "CONFIRMED",
    "clinicId": "c...",
    "dentistId": "c...",
    "dentalServiceId": "c...",
    "startsAt": "2026-11-12T18:00:00+00:00",
    "endsAt": "2026-11-12T18:30:00+00:00",
    "proposedStartsAt": null,
    "proposedEndsAt": null,
    "proposalExpiresAt": null,
    "timeZoneId": "America/Mexico_City"
  }
}
```

### `POST /api/appointments/{appointmentId}/proposal/reject`

Sin body. Response `200` con `status: "REJECTED"`, `startsAt/endsAt` originales y los tres campos propuestos conservados. `rejectedAt` y `rejectedBy` quedan persistidos y visibles en el detalle del consultorio.

### Errores

| HTTP | `error.code` | Caso |
|---|---|---|
| 401 | — | sin token o sin claim `sub` |
| 404 | — | cita inexistente, de otro paciente o de otro consultorio |
| 409 | `PROPOSAL_NOT_ACTIVE` | estado distinto de `PROPOSED` o campos de propuesta incompletos |
| 409 | `PROPOSAL_EXPIRED` | `ProposalExpiresAt <= now`; la cita queda `PENDING` |
| 409 | `SLOT_UNAVAILABLE` | intervalo ocupado, fuera de jornada, descanso o bloqueo |
| 409 | `DENTIST_UNAVAILABLE` | dentista inactivo o de otro consultorio |
| 409 | `SERVICE_CHANGED` | servicio inactivo, no ofrecido o duración distinta |
| 409 | `PATIENT_CONFLICT` | el paciente ya tiene una cita `CONFIRMED` en el intervalo |
| 409 | `APPOINTMENT_CONFLICT` | carrera de serialización o exclusión de agenda |

No se devuelve `403`: la pertenencia se resuelve con `sub` y la ausencia de pertenencia se reporta como `404` para no filtrar la existencia de la cita.

## Tests

| Requisito | Prueba |
|---|---|
| R1 | `AcceptProposal_ActiveAndFree_ReturnsConfirmedAndEvent` |
| R2 | `RejectProposal_Active_ReturnsRejectedAndRecordsPatient`; `GetProposal_AfterReject_ReturnsRejected` |
| R3 | `AcceptProposal_Occupied_Returns409AndKeepsProposed`; `AcceptProposal_BlockOverlap_Returns409`; `AcceptProposal_BreakOverlap_Returns409`; `AcceptProposal_OutsideSchedule_Returns409`; `AcceptProposal_PatientConflict_Returns409`; `AcceptProposal_Concurrent_OnlyOneConfirmed` |
| R4 | `IsExpired_RejectsAtOrAfterExpiry`; `IsExpired_AllowsBeforeExpiry`; `AcceptProposal_Expired_Returns409AndRevertsToPending`; `RejectProposal_Expired_Returns409AndRevertsToPending`; `AcceptProposal_NoActiveProposal_Returns409` |
| R5 | `GetProposal_ReturnsBothSchedules`; `GetProposal_ClinicAdminToken_Returns404` |
| R6 | `AcceptProposal_WithoutToken_Returns401`; `AcceptProposal_OtherPatient_Returns404`; `AcceptProposal_ClinicAdminToken_Returns404`; `RejectProposal_WithoutToken_Returns401`; `RejectProposal_OtherPatient_Returns404`; `RejectProposal_ClinicAdminToken_Returns404`; `GetProposal_WithoutToken_Returns401`; `GetProposal_OtherPatient_Returns404`. El caso feliz `AcceptProposal_ActiveAndFree_ReturnsConfirmedAndEvent` usa token sin rol. |
| R7 | `AcceptProposal_DentistUnavailable_Returns409`; `AcceptProposal_ServiceChanged_Returns409`; `AcceptProposal_BlockOverlap_Returns409`; `AcceptProposal_BreakOverlap_Returns409`; `AcceptProposal_OutsideSchedule_Returns409` |
| R8 | `AcceptProposal_ActiveAndFree_ReturnsConfirmedAndEvent`; `RejectProposal_Active_ReturnsRejectedAndRecordsPatient`; `AcceptProposal_Expired_Returns409AndRevertsToPending`; `ExpireProposal_SecondCycle_ReusesExpiredEvent` |
| R9 | Sin migración: build y suite sin cambios de esquema |
| R10 | Contrato cubierto por `GetProposal_ReturnsBothSchedules`, `GetProposal_AfterReject_ReturnsRejected` y las respuestas de aceptar/rechazar |

Archivos:

- `DentalConnect.Tests/AppointmentProposalRulesTests.cs`: dos casos nuevos de `IsExpired` (5/5 en el archivo).
- `DentalConnect.Tests/AppointmentProposalApiTests.cs`: casos API nuevos; reutiliza `PostgresApiFixture`, `TestAuthHandler` y el seed existente.
- `DentalConnect.Tests/PostgresApiFixture.cs`: sin cambios.
- `DentalConnect.Tests/TestAuthHandler.cs`: sin cambios.

El seed agrega `PatientSubject` a `SeedIds` y usa un subject determinista para que el cliente de paciente pueda operar la cita. Las pruebas API usan `[PostgresFact]` y se omiten sin `TEST_DATABASE_URL`; no se reportan como verdes.

## Criterios

- `dotnet build DentalConnect.slnx -c Release` → 0 errores / 0 advertencias.
- `dotnet test DentalConnect.slnx -c Release --filter FullyQualifiedName~AppointmentProposalRulesTests` → 5/5.
- `TEST_DATABASE_URL=... dotnet test DentalConnect.slnx -c Release` → N/N en verde; el conteo real se registra al cerrar.
- `dotnet test DentalConnect.slnx -c Release` sin base → pruebas API omitidas; se reporta como omitido, no como verde.
- Sin ciclo de migraciones: no hay cambio de esquema.
- Pruebas `401` y `404` por endpoint nuevo; ausencia de `403` documentada y probada con token de consultorio.
- `security-reviewer` PASS y `traceability-clerk` OK.
- Sin `TODO`; solo `NOTE (DENT-15)` y `NOTE (DENT-31)` vigentes. La nota de aceptación/rechazo ya se eliminó.

## Límites

- Barrido proactivo de propuestas vencidas y aviso al consultorio: TEC-07/DENT-31 (`NOTE (DENT-31)`). HU-14 solo detecta el vencimiento al responder.
- Notificación visible al consultorio: HU-15/DENT-15 (`NOTE (DENT-15)`), que es consulta in-app y pertenece al Sprint 08. En Sprint 07 el evento queda persistido en el outbox; email, SMS y push están fuera de alcance.
- Listado e historial de citas del paciente, con nombres de consultorio/dentista/servicio: HU-16/DENT-16.
- UI de aceptar/rechazar y comparación visual: PR separado en `DentalConnect-front` con `DENT-14`.
- Contrapropuesta del paciente, negociación por chat, cancelación y reprogramación: fuera de alcance.
- Conservar el horario original después de aceptar: fuera de alcance; la cita confirmada queda en el horario propuesto.
- Cambiar dentista o servicio dentro de la propuesta: fuera de alcance de HU-13/HU-14.

## Evidencia

| Fecha | Build | Tests | Migraciones | Commit | Notas |
|---|---|---|---|---|---|
| 11-Nov-2026 | `dotnet build DentalConnect.slnx -c Release` → 0 errores / 0 advertencias | `TEST_DATABASE_URL=... dotnet test DentalConnect.slnx -c Release` → 84/84 (38 no-Postgres + 46 API) | Sin cambio de esquema; sin migración | `aab9fda` specs, `e8260ca` T1, `108be29` T2–T3, `4862bc7` T4, `6497a3c` T5; cierre en HEAD | `security-reviewer` PASS con 4 warnings no bloqueantes; `traceability-clerk` contenido OK y gaps de cierre corregidos. Jira `DENT-14` en `Pruebas`; PR pendiente. |

## Aprobación

🚧 Borrador con evidencia. Pendiente de revisor indicado por el usuario.
