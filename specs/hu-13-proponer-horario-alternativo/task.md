# Tareas · HU-13 Proponer un horario alternativo

**Spec:** `spec.md`
**Plan:** `plan.md`
**Rama sugerida:** `feature/DENT-13-proponer-horario-alternativo` o `Sprint-7-<integrante>` según la convención vigente.
**Commits:** `Sprint 7: <resumen> (HU-13 · DENT-13)`.

## Checklist

- [ ] **T1. Dominio.** Campos `ProposedStartsAt`, `ProposedEndsAt`, `ProposalExpiresAt` en `Appointment` y `AppointmentProposalRules`.
  Verificación: `AppointmentProposalRulesTests` 3/3 y build Release 0/0.

- [ ] **T2. Migración.** `20261110120000_Sprint7AppointmentProposal` con Up/Down seguro; `CK_Appointment_Status` con `PROPOSED`; `CK_Appointment_Proposal`; fila en `docs/base-datos/migraciones.md`.
  Verificación: apply → revert a `20261109030000_Sprint6ClinicAppointmentReview` → re-apply sin errores.

- [ ] **T3. Infraestructura de pruebas y contrato.** `TestAuthHandler`, `PostgresApiFixture` (`ICollectionFixture`), seed determinista con `Folio`/`PatientSubject`/`IdempotencyKey` únicos; pruebas 401/403/404/409 definidas. No commitear la Fase A sola.
  Verificación: las pruebas quedan rojas por ruta ausente y pasan al cerrar T4: `Propose_WithoutToken_Returns401`, `Propose_WithoutClinicAdminRole_Returns403`, `Propose_AppointmentFromAnotherClinic_Returns404`, `Propose_InvalidStatus_Returns409`, `Propose_PastAlternative_Returns409`, `Propose_SameStartAsOriginal_Returns409`.

- [ ] **T4. Endpoint completo.** `POST /api/appointments/{appointmentId}/propose`, `RequireAuthorization`, `TryGetClinicContext`, validación de dentista/servicio/duración/jornada/descanso/bloqueos/`CONFIRMED`; estado `PROPOSED`, ambos horarios, expiración y evento `APPOINTMENT_ALTERNATIVE_PROPOSED`.
  Verificación: `Propose_AvailableAlternative_ReturnsProposedWithBothSchedulesAndEvent`, `Propose_SecondActiveProposal_Returns409`, `Propose_OccupiedInterval_Returns409AndDoesNotChangeAppointment`, `Propose_BreakOrBlockOverlap_Returns409`, `Propose_OutsideSchedule_Returns409`, `Propose_InactiveDentist_Returns409DentistUnavailable`, `Propose_ServiceDurationChanged_Returns409ServiceChanged`, `Propose_OriginalInPastButAlternativeFuture_Returns200`.

- [ ] **T5. Proyecciones.** Listado y detalle con `proposedStartsAt`, `proposedEndsAt`, `proposalExpiresAt`; `status=PROPOSED` aceptado. La prueba `ProposedStatusDoesNotBlockAvailability` va en `PublicAvailabilityRulesTests` y no requiere PostgreSQL.
  Verificación: `List_WithProposedStatus_ReturnsProposalFields`, `Detail_ReturnsOriginalAndProposedSchedules`, `ProposedStatusDoesNotBlockAvailability`.

- [ ] **T6. Concurrencia y evento.** Dos propuestas simultáneas dejan una sola `PROPOSED`; evento reutilizado tras volver a `PENDING` sin violar el índice único.
  Verificación: `Propose_ConcurrentRequests_OnlyOneProposed` y `Propose_ExpiredEvent_IsReused`.

- [ ] **T7. Verificación final.** Build Release 0/0; tests N/N; ciclo de migraciones verde con destinos explícitos o `database-migrations.yml`. La suite API solo corre con `TEST_DATABASE_URL`; en `deploy-aws.yml` se omite.
  Verificación: `dotnet build DentalConnect.slnx -c Release` y `dotnet test DentalConnect.slnx -c Release`.

- [ ] **T8. Guardas.** `security-reviewer` PASS y `traceability-clerk` OK.
  Verificación: reportes sin gaps; estado de la spec en `🚧 Borrador con evidencia`.

## Pendientes con dueño

| Pendiente | Dueño | Capa | Marca |
|---|---|---|---|
| Aceptar/rechazar propuesta y transición a `CONFIRMED`/`REJECTED` | HU-14 / DENT-14 | HU-14 | `NOTE (DENT-14)` |
| Reversión a `PENDING` al vencer y aviso al consultorio | TEC-07 / DENT-31 | TEC-07 | `NOTE (DENT-31)` |
| Entrega de la notificación al paciente | HU-15 / DENT-15 | HU-15 | `NOTE (DENT-15)` |
| UI de proponer horario y contrato camelCase | DENT-13 (DentalConnect-front) | Front | `DENT-13` |
| Historial de propuestas y contrapropuesta | Fuera de alcance | — | — |
| Cambiar servicio/dentista en la propuesta | Fuera de alcance | — | — |

## Evidencia

| Fecha | Build | Tests | Migraciones | Commit | Notas |
|---|---|---|---|---|---|
| PENDIENTE | PENDIENTE | PENDIENTE | PENDIENTE | PENDIENTE | Llenar con salida real al cerrar. |
