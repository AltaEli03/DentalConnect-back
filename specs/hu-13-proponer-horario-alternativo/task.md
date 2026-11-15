# Tareas · HU-13 Proponer un horario alternativo

**Spec:** `spec.md`
**Plan:** `plan.md`
**Rama:** `feature/DENT-13-proponer-horario-alternativo`.
**Commits:** rama `13b53b3..HEAD`; hitos: `13b53b3` specs, `b2cd9b6` dominio, `1e498dc` migración, `0c449bd` endpoint/pruebas, `4d93dd6` refuerzos, `487d217`/`db887c7`/`c6b4536` gates.

## Checklist

- [x] **T1. Dominio.** Campos `ProposedStartsAt`, `ProposedEndsAt`, `ProposalExpiresAt` en `Appointment` y `AppointmentProposalRules`.
  Verificación: `AppointmentProposalRulesTests` 3/3 y build Release 0/0.

- [x] **T2. Migración.** `20261110120000_Sprint7AppointmentProposal` con Up/Down seguro; `CK_Appointment_Status` con `PROPOSED`; `CK_Appointment_Proposal`; fila en `docs/base-datos/migraciones.md`.
  Verificación: apply → revert a `20261109030000_Sprint6ClinicAppointmentReview` → re-apply sin errores en PostgreSQL 16 local.

- [x] **T3. Infraestructura de pruebas y contrato.** `TestAuthHandler`, `PostgresApiFixture` (`ICollectionFixture`), seed determinista con `Folio`/`PatientSubject`/`IdempotencyKey` únicos; pruebas 401/403/404/409 definidas. No commitear la Fase A sola.
  Verificación: RED confirmado (17 fallos, 1 pase, 404); al cerrar T4 pasan `Propose_WithoutToken_Returns401`, `Propose_WithoutClinicAdminRole_Returns403`, `Propose_WithoutClinicClaim_Returns403`, `Propose_AppointmentFromAnotherClinic_Returns404`, `Propose_AppointmentFromAnotherClinicWithPastDate_Returns404`, `Propose_InvalidStatus_Returns409`, `Propose_PastAlternative_Returns409` y `Propose_SameStartAsOriginal_Returns409`.

- [x] **T4. Endpoint completo.** `POST /api/appointments/{appointmentId}/propose`, `RequireAuthorization`, `TryGetClinicContext`, validación de dentista/servicio/duración/jornada/descanso/bloqueos/`CONFIRMED`; estado `PROPOSED`, ambos horarios, expiración y evento `APPOINTMENT_ALTERNATIVE_PROPOSED`.
  Verificación: 21/21 pruebas API en verde.

- [x] **T5. Proyecciones.** Listado y detalle con `proposedStartsAt`, `proposedEndsAt`, `proposalExpiresAt`; `status=PROPOSED` aceptado. La prueba `ProposedStatusDoesNotBlockAvailability` va en `PublicAvailabilityRulesTests` y no requiere PostgreSQL.
  Verificación: `List_WithProposedStatus_ReturnsProposalFields`, `Detail_ReturnsOriginalAndProposedSchedules`, `ProposedStatusDoesNotBlockAvailability`.

- [x] **T6. Concurrencia y evento.** Dos propuestas simultáneas dejan una sola `PROPOSED`; evento reutilizado tras volver a `PENDING` sin violar el índice único.
  Verificación: `Propose_ConcurrentRequests_OnlyOneProposed` y `Propose_ExpiredEvent_IsReused`.

- [x] **T7. Verificación final.** Build Release 0/0; tests 57/57 (36 no-Postgres + 21 API); ciclo de migraciones verde con destinos explícitos. La suite API solo corre con `TEST_DATABASE_URL`; sin ella se reporta como skipped, no como verde.
  Verificación: `dotnet build DentalConnect.slnx -c Release` y `dotnet test DentalConnect.slnx -c Release` en verde.

- [x] **T8. Guardas.** `security-reviewer` PASS y `traceability-clerk` OK; el gap de Atlassian MCP es ambiental (DENT-13 verificado en la sesión padre).
  Verificación: reportes sin gaps de código/trazabilidad; estado de la spec en `🚧 Borrador con evidencia`.

## Pendientes con dueño

| Pendiente | Dueño | Capa | Marca |
|---|---|---|---|
| Reversión a `PENDING` al vencer y aviso al consultorio | TEC-07 / DENT-31 | TEC-07 | `NOTE (DENT-31)` |
| Entrega de la notificación al paciente | HU-15 / DENT-15 | HU-15 | `NOTE (DENT-15)` |
| UI de proponer horario y contrato camelCase | DENT-13 (DentalConnect-front) | Front | `DENT-13` |
| Historial de propuestas y contrapropuesta | Fuera de alcance | — | — |
| Cambiar servicio/dentista en la propuesta | Fuera de alcance | — | — |

## Evidencia

| Fecha | Build | Tests | Migraciones | Commit | Notas |
|---|---|---|---|---|---|
| 10-Nov-2026 | 0 errores / 0 advertencias | 57/57 (36 no-Postgres + 21 API) | apply → revert → reapply OK local | `4d93dd6` | Suite API con PostgreSQL 16 local; sin DB se marca skipped; CI de migraciones pendiente de push/PR. |
