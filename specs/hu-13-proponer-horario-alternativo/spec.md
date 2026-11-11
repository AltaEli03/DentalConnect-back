# HU-13 · Proponer un horario alternativo

| HU | Jira | Sprint | Módulo | Contraparte | Estado |
|---|---|---|---|---|---|
| HU-13 Proponer un horario alternativo | [DENT-13](https://uttt-proyecto.atlassian.net/browse/DENT-13) | DentalConnect Sprint 07 | `DentalConnect/AppointmentEndpoints.cs` (citas) | `DentalConnect-front` HU-13 (misma clave DENT-13; pendiente) | 🚧 Borrador con evidencia |

## Contexto

**Actor:** usuario autorizado del consultorio (`CLINIC_ADMIN`) dueño de la solicitud.
**Problema:** el horario original no conviene y el consultorio necesita ofrecer una alternativa sin confirmarla ni reservarla.
**Depende de:** HU-08/DENT-8 (disponibilidad), HU-10/DENT-10 (revisión de solicitudes), TEC-07/DENT-31 (eventos y vencimiento), HU-15/DENT-15 (notificaciones).
**Dependencias Jira verificadas:** DENT-14 (HU-14 Aceptar o rechazar un horario alternativo), DENT-15 (HU-15 Consultar las notificaciones de mis citas), DENT-31 (TEC-07 Eventos notificaciones y vencimientos).

## Requisitos

- **R1 (AC1).** Dado un `Appointment` en `PENDING`, cuando un `CLINIC_ADMIN` del consultorio propone un inicio disponible y distinto del original, entonces la solicitud pasa a `PROPOSED`, se conservan `StartsAt/EndsAt` originales y se persisten `ProposedStartsAt/ProposedEndsAt`. Responde `200` con ambos horarios.
- **R2 (AC2).** La alternativa se valida contra dentista activo, servicio activo y ofrecido, duración vigente, jornada, descanso, bloqueos y citas `CONFIRMED`. Si el intervalo está ocupado o fuera de jornada, responde `409 SLOT_UNAVAILABLE` y no guarda nada.
- **R3 (AC3).** Si ya existe una propuesta activa (`Status = PROPOSED`), otra llamada responde `409 PROPOSAL_ALREADY_ACTIVE` y no crea una segunda.
- **R4 (AC4).** Si `proposedStartsAt <= now` (UTC), responde `409 PAST_DATE` y no guarda nada.
- **R5.** `ProposalExpiresAt = min(now + 24 h, ProposedStartsAt)`. La aceptación/rechazo de la propuesta es de HU-14/DENT-14 (`NOTE (DENT-14)`). La reversión a `PENDING` y el aviso al consultorio al vencer son de TEC-07/DENT-31 (`NOTE (DENT-31)`).
- **R6.** En la misma transacción se registra el evento outbox `APPOINTMENT_ALTERNATIVE_PROPOSED` para la cita. Si ya existe, se reutiliza y se marca no procesado. La entrega al paciente y el worker son de HU-15/DENT-15 (`NOTE (DENT-15)`).
- **R7.** Solo el `CLINIC_ADMIN` del consultorio dueño puede proponer. Sin token: `401`. Sin rol o claim de consultorio: `403`. Cita inexistente o de otro consultorio: `404` sin datos.
- **R8.** La propuesta **no reserva** el intervalo: disponibilidad pública y creación de citas siguen bloqueando solo `CONFIRMED`. La aceptación de HU-14 revalida.
- **R9.** La alternativa debe ser distinta del inicio original; si coincide, `409 PROPOSAL_SAME_AS_ORIGINAL`.

## Diseño

### Estado

```text
PENDING --propose--> PROPOSED
PROPOSED --HU-14 acepta--> CONFIRMED
PROPOSED --HU-14 rechaza--> REJECTED
PROPOSED --TEC-07 vence--> PENDING
PENDING --HU-11/12--> CONFIRMED | REJECTED
```

`PROPOSED` no bloquea disponibilidad. Solo `CONFIRMED` participa en las exclusiones de agenda.

### Dominio

En `DentalConnect.Domain/Entities/Entities.cs`, agregar a `Appointment`:

- `DateTimeOffset? ProposedStartsAt`
- `DateTimeOffset? ProposedEndsAt`
- `DateTimeOffset? ProposalExpiresAt`

En `DentalConnect.Domain/Entities/AppointmentScheduling.cs`, crear reglas puras:

- `AppointmentProposalRules.ExpiresAt(DateTimeOffset now, DateTimeOffset proposedStart)`
- `AppointmentProposalRules.IsPast(DateTimeOffset now, DateTimeOffset start)`

Sin EF ni ASP.NET.

### Persistencia

`DentalConnectDbContext` mapea las propiedades por convención. Migración manual `20261110120000_Sprint7AppointmentProposal.cs`:

- reemplaza `CK_Appointment_Status` para incluir `PROPOSED`;
- agrega las tres columnas nullable;
- agrega `CK_Appointment_Proposal`: las tres nulas o las tres no nulas con `ProposedStartsAt < ProposedEndsAt`;
- `Down`: `UPDATE "Appointment" SET "Status"='PENDING' WHERE "Status"='PROPOSED'`, restaura el CHECK viejo y elimina columnas.

No usar `dotnet ef migrations add`: el snapshot EF está desincronizado y no incluye `Appointment`.

### Endpoint

En `DentalConnect/AppointmentEndpoints.cs`:

- `AppointmentStatuses` incluye `PROPOSED`;
- `MapPost("/api/appointments/{appointmentId}/propose", ProposeAsync).RequireAuthorization()`;
- `ProposeAsync` reutiliza `TryGetClinicContext`;
- transacción `Serializable`;
- valida estado, pasado, mismo horario, dentista/servicio/duración y `PublicAvailabilityRules.Generate(day, 1, duration, schedules, blocks, zone, now, confirmed)`;
- guarda estado, campos y evento; `SaveChanges` + `Commit`;
- captura `40001`/`23505` como `409 APPOINTMENT_CONFLICT`.

La propuesta no reserva: no se tocan `CreateAsync`, `ConfirmAsync`, `RejectAsync`, `PublicAvailabilityEndpoints` ni las restricciones de exclusión.

### Proyecciones

El helper `AppointmentResponse`, el listado y el detalle agregan `proposedStartsAt`, `proposedEndsAt`, `proposalExpiresAt`. `startsAt/endsAt` siguen siendo el horario original.

### Documentación

Fila de la migración en `docs/base-datos/migraciones.md`. El README de citas queda como deuda preexistente.

## Contratos

### `POST /api/appointments/{appointmentId}/propose`

Request:

```json
{ "proposedStartsAt": "2026-11-12T18:00:00Z" }
```

Response `200` (ejemplo con `now = 2026-11-11T12:00:00Z`):

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

Errores:

| HTTP | `error.code` | Caso |
|---|---|---|
| 400 | `VALIDATION_ERROR` | body nulo, falta `proposedStartsAt` o fecha inválida |
| 401 | — | sin token |
| 403 | — | sin `CLINIC_ADMIN` o claim de consultorio |
| 404 | — | cita inexistente o de otro consultorio |
| 409 | `INVALID_STATUS` | estado `CONFIRMED`, `REJECTED` o `CANCELLED` |
| 409 | `PROPOSAL_ALREADY_ACTIVE` | estado `PROPOSED` |
| 409 | `PAST_DATE` | inicio propuesto en el pasado |
| 409 | `PROPOSAL_SAME_AS_ORIGINAL` | mismo inicio que el original |
| 409 | `DENTIST_UNAVAILABLE` | dentista inactivo o de otro consultorio |
| 409 | `SERVICE_CHANGED` | servicio inactivo, no ofrecido o duración distinta |
| 409 | `SLOT_UNAVAILABLE` | fuera de jornada, descanso, bloqueo o cita confirmada |
| 409 | `APPOINTMENT_CONFLICT` | conflicto de serialización o unicidad |

### Listado y detalle

- `GET /api/appointments?status=PROPOSED` acepta el estado.
- Cada item agrega `proposedStartsAt`, `proposedEndsAt`, `proposalExpiresAt`.
- El detalle mantiene `startsAt/endsAt` como originales y agrega los mismos tres campos.

## Tests

| Requisito | Prueba |
|---|---|
| R1 | `Propose_AvailableAlternative_ReturnsProposedWithBothSchedulesAndEvent`; `Propose_OriginalInPastButAlternativeFuture_Returns200` |
| R2 | `Propose_OccupiedInterval_Returns409AndDoesNotChangeAppointment`; `Propose_BreakOrBlockOverlap_Returns409`; `Propose_OutsideSchedule_Returns409`; `Propose_InactiveDentist_Returns409DentistUnavailable`; `Propose_InactiveService_Returns409ServiceChanged`; `Propose_ServiceDurationChanged_Returns409ServiceChanged` |
| R3 | `Propose_SecondActiveProposal_Returns409`; `Propose_ConcurrentRequests_OnlyOneProposed` |
| R4 | `Propose_PastAlternative_Returns409`; `IsPast_RejectsNowAndEarlier` |
| R5 | `ExpiresAt_Uses24HoursWhenProposedStartIsLater`; `ExpiresAt_UsesProposedStartWhenSooner` |
| R6 | `Propose_AvailableAlternative_ReturnsProposedWithBothSchedulesAndEvent`; `Propose_ExpiredEvent_IsReused` |
| R7 | `Propose_WithoutToken_Returns401`; `Propose_WithoutClinicAdminRole_Returns403`; `Propose_WithoutClinicClaim_Returns403`; `Propose_AppointmentFromAnotherClinic_Returns404`; `Propose_AppointmentFromAnotherClinicWithPastDate_Returns404`; `Propose_InvalidStatus_Returns409` |
| R8 | `ProposedStatusDoesNotBlockAvailability` |
| R9 | `Propose_SameStartAsOriginal_Returns409` |
| Contrato | `List_WithProposedStatus_ReturnsProposalFields`; `Detail_ReturnsOriginalAndProposedSchedules` |

- `DentalConnect.Tests/AppointmentProposalRulesTests.cs` (nuevo).
- `DentalConnect.Tests/TestAuthHandler.cs` (nuevo, autenticación de prueba).
- `DentalConnect.Tests/PostgresApiFixture.cs` (nuevo, host API + PostgreSQL de CI).
- `DentalConnect.Tests/AppointmentProposalApiTests.cs` (nuevo; PostgreSQL `TEST_DATABASE_URL`; skip si falta).
- `DentalConnect.Tests/PublicAvailabilityRulesTests.cs`: agregar caso `PROPOSED`.
- Migración: ciclo apply → revert → re-apply con destinos explícitos o `database-migrations.yml`.

## Criterios

- `dotnet build DentalConnect.slnx -c Release` → 0 errores / 0 advertencias.
- `dotnet test DentalConnect.slnx -c Release` → N/N en verde (conteo real al cerrar).
- Ciclo de migraciones en verde.
- Prueba 401/403 por endpoint nuevo.
- `security-reviewer` PASS y `traceability-clerk` OK.
- Sin `TODO`; solo `NOTE (DENT-14)`, `NOTE (DENT-15)`, `NOTE (DENT-31)`.

## Límites

- Aceptar/rechazar la propuesta: HU-14/DENT-14.
- Reversión por vencimiento y aviso al consultorio: TEC-07/DENT-31.
- Entrega de la notificación al paciente: HU-15/DENT-15.
- Historial de propuestas y contrapropuesta del paciente: fuera de alcance.
- Cambiar servicio o dentista en la propuesta: fuera de alcance.
- UI del front: PR separado en `DentalConnect-front` con la misma clave DENT-13.
- README general de citas: fuera de alcance de esta HU.

## Evidencia

| Campo | Valor |
|---|---|
| Fecha | 10-Nov-2026 |
| Build | `dotnet build DentalConnect.slnx -c Release` → 0 errores / 0 advertencias |
| Tests | `TEST_DATABASE_URL=... dotnet test DentalConnect.slnx -c Release` → 57/57 (36 no-Postgres + 21 API con PostgreSQL) |
| Tests sin DB | `dotnet test DentalConnect.slnx -c Release` → 36 passed / 21 skipped |
| Migraciones | apply → revert → reapply OK en PostgreSQL 16 local |
| Commits | rama `13b53b3..HEAD`; hitos: specs `13b53b3`, dominio `b2cd9b6`, migración `1e498dc`, implementación `0c449bd`, refuerzos `4d93dd6`, gates `487d217`/`db887c7`/`c6b4536` |
| Suite API | corre con `TEST_DATABASE_URL` en `sprint5-tests.yml`; `deploy-aws.yml` la omite |
| CI de migraciones | PENDIENTE (sin push/PR) |

## Aprobación

🚧 Borrador con evidencia. Pendiente de revisor indicado por el usuario.
