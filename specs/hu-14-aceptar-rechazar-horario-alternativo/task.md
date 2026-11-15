# Tareas · HU-14 Aceptar o rechazar un horario alternativo

**Spec:** `spec.md`
**Plan:** `plan.md`
**Rama:** `feature/DENT-14-aceptar-rechazar-horario-alternativo`.
**Jira:** [DENT-14](https://uttt-proyecto.atlassian.net/browse/DENT-14) · Sprint `DentalConnect Sprint 07`.
**Contraparte:** `DentalConnect-front` HU-14, misma clave `DENT-14` (pendiente).

## Checklist

- [x] **T0. Specs.** `spec.md`, `plan.md` y `task.md` con cabecera DENT-14, requisitos, contratos, pruebas y límites.
  Verificación: commit `aab9fda`; `traceability-clerk` OK.

- [x] **T1. Regla de vencimiento.** `AppointmentProposalRules.IsExpired(now, expiresAt)` con borde `<=`; dos pruebas nuevas.
  Verificación: `dotnet test DentalConnect.slnx -c Release --filter FullyQualifiedName~AppointmentProposalRulesTests` → 5/5. Suite completa sin `TEST_DATABASE_URL` → 38 passed / 21 skipped.

- [x] **T2. Seed y RED de autorización.** `SeedIds.PatientSubject` determinista; pruebas RED de `401`, `404` por endpoint, token de consultorio y `PROPOSAL_NOT_ACTIVE`; control positivo con el dueño.
  Verificación: RED confirmado con PostgreSQL 16: 4 failed / 26 passed; los `401` reciben `404` de ruta ausente y el control positivo `AcceptProposal_NoActiveProposal_Returns409` falla con `404`. Los `404` que pasan en falso quedan cubiertos por ese control. Al cerrar T3: 34/34 pruebas API verdes. No commit de la Fase RED sola; T3 commitea ambos.

- [x] **T3. Núcleo aceptar/rechazar.** Rutas `POST .../proposal/accept` y `/reject`, `RespondToProposalAsync`, ownership por `sub`, transacción `Serializable`, guard de vencimiento, eventos `APPOINTMENT_ALTERNATIVE_ACCEPTED`, `APPOINTMENT_ALTERNATIVE_REJECTED` y `APPOINTMENT_ALTERNATIVE_EXPIRED`.
  Verificación: aceptar mueve `StartsAt/EndsAt` y limpia propuesta; rechazar conserva propuesta y registra `RejectedAt/RejectedBy`; vencido vuelve a `PENDING`. El caso feliz usa token sin rol `PATIENT`. Suite API 34/34 y suite completa 72/72 con PostgreSQL 16.
  Ruling: `GET /proposal` se implementó en T3, no en T5, para cerrar en verde las pruebas 401/404 de T2; T5 solo agrega las pruebas de contrato camelCase.

- [x] **T4. Revalidación y concurrencia.** Aceptar revalida dentista, servicio, duración, jornada, descanso, bloqueos, citas `CONFIRMED` y choque del paciente. Conflictos dejan `PROPOSED`; dos aceptaciones simultáneas dejan una sola `CONFIRMED`; el segundo ciclo de vencimiento reutiliza el evento.
  Verificación: 9/9 pruebas T4 en verde; suite API 43/43; suite completa 81/81; build Release 0/0. `AcceptProposal_Occupied_Returns409AndKeepsProposed`, `AcceptProposal_BlockOverlap_Returns409`, `AcceptProposal_BreakOverlap_Returns409`, `AcceptProposal_OutsideSchedule_Returns409`, `AcceptProposal_DentistUnavailable_Returns409`, `AcceptProposal_ServiceChanged_Returns409`, `AcceptProposal_PatientConflict_Returns409`, `AcceptProposal_Concurrent_OnlyOneConfirmed`, `ExpireProposal_SecondCycle_ReusesExpiredEvent`.

- [x] **T5. Lectura de propuesta.** `GET /api/appointments/{appointmentId}/proposal` con `AppointmentResponse`; sin efectos secundarios; ownership por `sub`.
  Verificación: 2/2 pruebas de contrato; suite API 45/45; suite completa 83/83; build Release 0/0. `GetProposal_ReturnsBothSchedules`, `GetProposal_AfterReject_ReturnsRejected`, `GetProposal_WithoutToken_Returns401`, `GetProposal_OtherPatient_Returns404`, `GetProposal_ClinicAdminToken_Returns404`.
  Ruling: sin RED posible; la ruta se adelantó a T3 para cerrar la autorización de T2. Las pruebas fijan camelCase y que el `GET` no mute estado.

- [x] **T6. Cierre.** Build Release 0/0; suite real con `TEST_DATABASE_URL`; sin cambio de esquema; retirar `NOTE (DENT-14)` del código y de las cinco referencias de HU-13; transicionar `DENT-14` a `Pruebas`; `security-reviewer` PASS y `traceability-clerk` contenido OK; evidencia con conteos reales.
  Verificación: `dotnet build DentalConnect.slnx -c Release` → 0/0; `TEST_DATABASE_URL=... dotnet test DentalConnect.slnx -c Release` → 84/84 (38 no-Postgres + 46 API); `grep -rn "NOTE (DENT-14)" DentalConnect DentalConnect.Domain DentalConnect.Infrastructure DentalConnect.Tests` vacío; `grep -rn "NOTE (DENT-14)" specs/hu-13-proponer-horario-alternativo` vacío; `git diff --stat main -- DentalConnect.Infrastructure/Persistence/Migrations docs/base-datos/migraciones.md` vacío; Jira `DENT-14` en `Pruebas` (transición 41) con PR pendiente.

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
| 11-Nov-2026 | 0 errores / 0 advertencias | 84/84 (38 no-Postgres + 46 API) | Sin cambio de esquema | `aab9fda` specs, `e8260ca` T1, `108be29` T2–T3, `4862bc7` T4, `6497a3c` T5; cierre en HEAD | PostgreSQL 16 local; `security-reviewer` PASS; `traceability-clerk` contenido OK; Jira en `Pruebas`; PR pendiente. |
