# Tareas · HU-14 Aceptar o rechazar un horario alternativo

**Spec:** `spec.md`
**Plan:** `plan.md`
**Rama:** `feature/DENT-14-aceptar-rechazar-horario-alternativo`.
**Jira:** [DENT-14](https://uttt-proyecto.atlassian.net/browse/DENT-14) · Sprint `DentalConnect Sprint 07`.
**Contraparte:** `DentalConnect-front` HU-14, misma clave `DENT-14` (pendiente).

## Checklist

- [ ] **T0. Specs.** `spec.md`, `plan.md` y `task.md` con cabecera DENT-14, requisitos, contratos, pruebas y límites.
  Verificación: `git diff --check` sin errores; revisión de que el summary coincide con Jira.

- [ ] **T1. Regla de vencimiento.** `AppointmentProposalRules.IsExpired(now, expiresAt)` con borde `<=`; dos pruebas nuevas.
  Verificación: `dotnet test DentalConnect.slnx -c Release --filter FullyQualifiedName~AppointmentProposalRulesTests` → 5/5.

- [ ] **T2. Seed y RED de autorización.** `SeedIds.PatientSubject` determinista; pruebas RED de `401`, `404` por endpoint, token de consultorio y `PROPOSAL_NOT_ACTIVE`; control positivo con el dueño.
  Verificación: el control positivo falla con `404` de ruta ausente, no por compilación; los `404` que pasan en falso se aceptan solo si el control positivo falla. No commit de la Fase RED sola.

- [ ] **T3. Núcleo aceptar/rechazar.** Rutas `POST .../proposal/accept` y `/reject`, `RespondToProposalAsync`, ownership por `sub`, transacción `Serializable`, guard de vencimiento, eventos `APPOINTMENT_ALTERNATIVE_ACCEPTED`, `APPOINTMENT_ALTERNATIVE_REJECTED` y `APPOINTMENT_ALTERNATIVE_EXPIRED`.
  Verificación: aceptar mueve `StartsAt/EndsAt` y limpia propuesta; rechazar conserva propuesta y registra `RejectedAt/RejectedBy`; vencido vuelve a `PENDING`. El caso feliz usa token sin rol `PATIENT`.

- [ ] **T4. Revalidación y concurrencia.** Aceptar revalida dentista, servicio, duración, jornada, descanso, bloqueos, citas `CONFIRMED` y choque del paciente. Conflictos dejan `PROPOSED`; dos aceptaciones simultáneas dejan una sola `CONFIRMED`; el segundo ciclo de vencimiento reutiliza el evento.
  Verificación: `AcceptProposal_Occupied_Returns409AndKeepsProposed`, `AcceptProposal_BlockOverlap_Returns409`, `AcceptProposal_BreakOverlap_Returns409`, `AcceptProposal_OutsideSchedule_Returns409`, `AcceptProposal_DentistUnavailable_Returns409`, `AcceptProposal_ServiceChanged_Returns409`, `AcceptProposal_PatientConflict_Returns409`, `AcceptProposal_Concurrent_OnlyOneConfirmed`, `ExpireProposal_SecondCycle_ReusesExpiredEvent`.

- [ ] **T5. Lectura de propuesta.** `GET /api/appointments/{appointmentId}/proposal` con `AppointmentResponse`; sin efectos secundarios; ownership por `sub`.
  Verificación: `GetProposal_ReturnsBothSchedules`, `GetProposal_AfterReject_ReturnsRejected`, `GetProposal_WithoutToken_Returns401`, `GetProposal_OtherPatient_Returns404`, `GetProposal_ClinicAdminToken_Returns404`.

- [ ] **T6. Cierre.** Build Release 0/0; suite real con `TEST_DATABASE_URL`; sin cambio de esquema; retirar `NOTE (DENT-14)` del código y de las cinco referencias de HU-13; transicionar `DENT-14` y enlazar el PR por Atlassian MCP; `security-reviewer` PASS y `traceability-clerk` OK; evidencia con conteos reales.
  Verificación: `dotnet build DentalConnect.slnx -c Release` y `TEST_DATABASE_URL=... dotnet test DentalConnect.slnx -c Release`; `grep -rn "NOTE (DENT-14)" DentalConnect DentalConnect.Domain DentalConnect.Infrastructure DentalConnect.Tests` vacío; `grep -rn "NOTE (DENT-14)" specs/hu-13-proponer-horario-alternativo` vacío; `git diff --stat main -- DentalConnect.Infrastructure/Persistence/Migrations docs/base-datos/migraciones.md` vacío.

## Pendientes con dueño

| Pendiente | Dueño | Capa | Marca |
|---|---|---|---|
| Barrido proactivo de propuestas vencidas y aviso al consultorio | TEC-07 / DENT-31 | TEC-07 | `NOTE (DENT-31)` |
| Visualización in-app de la notificación (Sprint 08); email/SMS/push fuera de alcance | HU-15 / DENT-15 | HU-15 | `NOTE (DENT-15)` |
| Listado e historial de citas del paciente, con nombres de consultorio/dentista/servicio | HU-16 / DENT-16 | HU-16 | `DENT-16` |
| UI de paciente para comparar, aceptar y rechazar | DENT-14 (DentalConnect-front) | Front | `DENT-14` |
| Auditoría del horario original después de aceptar | Fuera de alcance | — | — |
| Contrapropuesta, chat, cancelación y reprogramación | Fuera de alcance | — | — |

## Evidencia

| Fecha | Build | Tests | Migraciones | Commit | Notas |
|---|---|---|---|---|---|
| 11-Nov-2026 | Pendiente | Pendiente | Sin cambio de esquema | Pendiente | Specs en borrador; implementación no iniciada. |
