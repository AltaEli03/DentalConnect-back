# HU-14 Aceptar o rechazar un horario alternativo Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Permitir que el paciente dueño de una solicitud `PROPOSED` compare ambos horarios, acepte la alternativa (pasando a `CONFIRMED` de forma atómica) o la rechace (pasando a `REJECTED` con actor paciente), y que una propuesta vencida no pueda confirmarse.

**Architecture:** Se agregan tres rutas al módulo de citas existente. Aceptar y rechazar comparten un núcleo transaccional `Serializable` que verifica propiedad por `sub`, estado `PROPOSED` y vencimiento; aceptar revalida la disponibilidad y mueve `StartsAt/EndsAt` al horario propuesto. Los eventos se guardan en el outbox existente. No hay cambio de esquema.

**Tech Stack:** .NET 10, ASP.NET Core Minimal APIs, EF Core 10, PostgreSQL 16, xUnit 2.9, WebApplicationFactory, `TEST_DATABASE_URL`.

**Spec:** `specs/hu-14-aceptar-rechazar-horario-alternativo/spec.md`

## Global Constraints

- Estados en UPPER_SNAKE: `PENDING`, `PROPOSED`, `CONFIRMED`, `REJECTED`.
- Sin migración: reutilizar `ProposedStartsAt`, `ProposedEndsAt`, `ProposalExpiresAt`, `RejectedAt`, `RejectedBy`, `ClinicNotificationEvent`.
- Sin paquetes NuGet nuevos.
- Autorización por propiedad: `Appointment.PatientSubject == sub`; sin token o sin `sub` → `401`; otro paciente o token de consultorio → `404`.
- No agregar rol `PATIENT` ni `403`; documentar la decisión en la spec.
- Aceptar y rechazar usan transacción `Serializable`.
- Aceptar revalida dentista, servicio, duración, jornada, descanso, bloqueos, citas `CONFIRMED` y choque del mismo paciente.
- Rechazar no revalida disponibilidad.
- `ProposalExpiresAt <= now` → `409 PROPOSAL_EXPIRED`, reversión perezosa a `PENDING`, limpieza de propuesta y evento `APPOINTMENT_ALTERNATIVE_EXPIRED`.
- Eventos: `APPOINTMENT_ALTERNATIVE_ACCEPTED`, `APPOINTMENT_ALTERNATIVE_REJECTED`, `APPOINTMENT_ALTERNATIVE_EXPIRED`; únicos por `(AppointmentId, EventType)`.
- Zona del piloto `America/Mexico_City`; persistir UTC.
- No tocar `CreateAsync`, `ConfirmAsync`, `RejectAsync` ni la lógica de `ProposeAsync` de HU-13, salvo retirar `NOTE (DENT-14)` al cerrar.
- Sin `TODO`; solo `NOTE (DENT-15)` y `NOTE (DENT-31)`.
- Build Release con 0 errores y 0 advertencias.
- Pruebas API solo con `TEST_DATABASE_URL`; sin ella se omiten y no se reportan como verdes.

## Review Focus

1. Vencimiento exacto: `ProposalExpiresAt == now` debe contar como vencido (`<=`), revertir a `PENDING` y encolar un solo evento `EXPIRED`.
2. Doble aceptación concurrente: exactamente una `200`, una `409`; una sola cita `CONFIRMED` y un solo evento `ACCEPTED`.
3. Intervalo ocupado después de proponer: aceptar no debe confirmar ni dejar un evento `ACCEPTED`; la cita sigue `PROPOSED`.
4. Propiedad: un token de otro paciente o de consultorio no debe distinguir entre cita inexistente y ajena; ambos `404`.
5. Aceptación con choque del mismo paciente o del dentista: la transacción debe fallar completa y no mover `StartsAt/EndsAt`.

Cada línea tiene su prueba en la tarea que implementa el código correspondiente.

## File Structure

| Archivo | Responsabilidad |
|---|---|
| `specs/hu-14-aceptar-rechazar-horario-alternativo/spec.md` | Requisitos, contratos, pruebas y límites. |
| `specs/hu-14-aceptar-rechazar-horario-alternativo/plan.md` | Este plan. |
| `specs/hu-14-aceptar-rechazar-horario-alternativo/task.md` | Checklist y evidencia. |
| `DentalConnect.Domain/Entities/AppointmentScheduling.cs` | Regla pura `IsExpired`. |
| `DentalConnect/AppointmentEndpoints.cs` | Rutas, ownership, transacción, revalidación, eventos y proyección reutilizada. |
| `DentalConnect.Tests/AppointmentProposalRulesTests.cs` | Pruebas de la regla de vencimiento. |
| `DentalConnect.Tests/AppointmentProposalApiTests.cs` | Flujos HTTP, permisos, conflictos y concurrencia. |
| `specs/hu-13-proponer-horario-alternativo/{spec.md,task.md}` | Conciliación al cerrar: retirar el pendiente consumido por HU-14. |

---

### Task 0: Commit de specs

**Files:**
- Create: `specs/hu-14-aceptar-rechazar-horario-alternativo/spec.md`
- Create: `specs/hu-14-aceptar-rechazar-horario-alternativo/plan.md`
- Create: `specs/hu-14-aceptar-rechazar-horario-alternativo/task.md`

- [ ] **Step 1: Revisar que la cabecera y el summary coincidan con `DENT-14`**

Comprobar que el título sea `HU-14 Aceptar o rechazar un horario alternativo`, Sprint `DentalConnect Sprint 07` y la contraparte `DentalConnect-front` con `DENT-14`.

- [ ] **Step 2: Commit**

```bash
git add specs/hu-14-aceptar-rechazar-horario-alternativo
git commit -m "Sprint 7: especificar respuesta a propuesta de horario (HU-14 · DENT-14)"
```

---

### Task 1: Regla pura de vencimiento

**Files:**
- Modify: `DentalConnect.Domain/Entities/AppointmentScheduling.cs:3-8`
- Test: `DentalConnect.Tests/AppointmentProposalRulesTests.cs`

**Interfaces:**
- Consumes: nada.
- Produces: `AppointmentProposalRules.IsExpired(DateTimeOffset now, DateTimeOffset expiresAt) -> bool`.

- [ ] **Step 1: Write the failing tests**

Agregar a `AppointmentProposalRulesTests.cs`:

```csharp
[Fact]
public void IsExpired_RejectsAtOrAfterExpiry()
{
    Assert.True(AppointmentProposalRules.IsExpired(Now, Now));
    Assert.True(AppointmentProposalRules.IsExpired(Now, Now.AddSeconds(-1)));
}

[Fact]
public void IsExpired_AllowsBeforeExpiry()
{
    Assert.False(AppointmentProposalRules.IsExpired(Now, Now.AddSeconds(1)));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```bash
dotnet test DentalConnect.slnx -c Release --filter FullyQualifiedName~AppointmentProposalRulesTests
```

Expected: FAIL de compilación porque `IsExpired` no existe.

- [ ] **Step 3: Implement the minimum rule**

En `AppointmentProposalRules`:

```csharp
public static bool IsExpired(DateTimeOffset now, DateTimeOffset expiresAt) => expiresAt <= now;
```

- [ ] **Step 4: Run tests to verify they pass**

Run:

```bash
dotnet test DentalConnect.slnx -c Release --filter FullyQualifiedName~AppointmentProposalRulesTests
```

Expected: 5/5 PASS (3 existentes + 2 nuevos).

- [ ] **Step 5: Commit**

```bash
git add DentalConnect.Domain/Entities/AppointmentScheduling.cs DentalConnect.Tests/AppointmentProposalRulesTests.cs
git commit -m "Sprint 7: agregar regla de vencimiento de propuesta (HU-14 · DENT-14)"
```

---

### Task 2: Seed de paciente y pruebas RED de autorización

**Files:**
- Modify: `DentalConnect.Tests/AppointmentProposalApiTests.cs:20-31,435-588`

**Interfaces:**
- Consumes: seed y helpers existentes.
- Produces:
  - `SeedIds.PatientSubject` (`string`).
  - `SeedAsync(...)` crea la cita principal con `PatientSubject = "test-patient"`.
  - `NewAppointment(..., string? patientSubject = null)` permite fijar el subject.
  - `NewClient(string? role = "CLINIC_ADMIN", ...)` permite un token autenticado sin rol; los casos felices usan `NewClient(null, subject: ids.PatientSubject)`.

- [ ] **Step 1: Add the deterministic patient subject to the seed**

Agregar `string PatientSubject` al record `SeedIds` y una constante:

```csharp
private const string PatientSubject = "test-patient";
```

En `SeedAsync`, crear la cita principal con `NewAppointment(..., patientSubject: PatientSubject)`. En `NewAppointment`, agregar el parámetro opcional y usarlo en `PatientSubject`. La cita de la otra clínica puede usar `"other-patient"`.

- [ ] **Step 2: Write the failing authorization/status tests**

Agregar estos casos con `[PostgresFact]`:

```csharp
[PostgresFact] public async Task AcceptProposal_WithoutToken_Returns401()
[PostgresFact] public async Task AcceptProposal_OtherPatient_Returns404()
[PostgresFact] public async Task AcceptProposal_ClinicAdminToken_Returns404()
[PostgresFact] public async Task RejectProposal_WithoutToken_Returns401()
[PostgresFact] public async Task RejectProposal_OtherPatient_Returns404()
[PostgresFact] public async Task GetProposal_WithoutToken_Returns401()
[PostgresFact] public async Task GetProposal_OtherPatient_Returns404()
[PostgresFact] public async Task GetProposal_ClinicAdminToken_Returns404()
[PostgresFact] public async Task AcceptProposal_NoActiveProposal_Returns409()
```

Aserciones mínimas:

- Sin token: `_fixture.Factory!.CreateClient()` sin `X-Test-Sub` → `401`.
- Otro paciente: `NewClient(null, subject: "other-patient")` → `404`.
- Token de consultorio: `NewClient("CLINIC_ADMIN", ids.ClinicId)` → `404`.
- Sin propuesta activa: cita `PENDING`, aceptar con el subject dueño y token sin rol → `409 PROPOSAL_NOT_ACTIVE`.
- Para los casos de rechazo y `GET`, crear primero la propuesta con el cliente `CLINIC_ADMIN` y luego llamar con el cliente de paciente.
- Si `TEST_DATABASE_URL` no existe, cada `[PostgresFact]` se omite.

- [ ] **Step 3: Run tests to verify they fail for the right reason**

Run:

```bash
TEST_DATABASE_URL=... dotnet test DentalConnect.slnx -c Release --filter FullyQualifiedName~AppointmentProposalApiTests
```

Expected: FAIL porque las rutas nuevas aún no existen. Los casos que esperan `404` pueden pasar en falso; el control positivo es `AcceptProposal_NoActiveProposal_Returns409`, que debe fallar con `404` (no con `409`) hasta implementar la ruta. No debe fallar la compilación del seed.

- [ ] **Step 4: Do not commit yet**

La Fase RED no se commitea sola. Task 3 implementa el núcleo y commitea ambos.

---

### Task 3: Núcleo de aceptar/rechazar y eventos

**Files:**
- Modify: `DentalConnect/AppointmentEndpoints.cs:17-31,240-305`
- Modify: `DentalConnect.Tests/AppointmentProposalApiTests.cs` (creado en Task 2)
- Incluir en el commit: `DentalConnect.Tests/AppointmentProposalApiTests.cs`

**Interfaces:**
- Consumes: `AppointmentProposalRules.IsExpired`, `PublicAvailabilityRules.Generate`, `ClinicNotificationEvent`, `SqlState`.
- Produces:
  - Rutas `POST /api/appointments/{appointmentId}/proposal/accept` y `/reject`.
  - Ruta `GET /api/appointments/{appointmentId}/proposal` y `GetProposalAsync` (adelantada desde Task 5 para que las pruebas de autorización de Task 2 cierren en verde).
  - `RespondToProposalAsync(string appointmentId, bool accept, HttpContext http, DentalConnectDbContext db, CancellationToken ct) -> Task<IResult>`.
  - `TryGetPatientSubject(HttpContext http, out string subject) -> bool`.
  - `QueueClinicEventAsync(DentalConnectDbContext db, string appointmentId, string eventType, DateTimeOffset now, CancellationToken ct) -> Task`.
  - Constantes `AlternativeAcceptedEvent`, `AlternativeRejectedEvent`, `AlternativeExpiredEvent`.

- [ ] **Step 1: Write the failing success/reject/expiry tests**

Agregar:

```csharp
[PostgresFact] public async Task AcceptProposal_ActiveAndFree_ReturnsConfirmedAndEvent()
[PostgresFact] public async Task RejectProposal_Active_ReturnsRejectedAndRecordsPatient()
[PostgresFact] public async Task AcceptProposal_Expired_Returns409AndRevertsToPending()
[PostgresFact] public async Task RejectProposal_Expired_Returns409AndRevertsToPending()
```

Aserciones:

- Aceptar: cliente `NewClient(null, subject: ids.PatientSubject)` (token sin rol); `200`; `status == "CONFIRMED"`; `startsAt == ids.ProposedStartsAt`; `endsAt == ids.ProposedEndsAt`; `proposedStartsAt`, `proposedEndsAt` y `proposalExpiresAt` nulos; una fila `ClinicNotificationEvent` con `EventType == "APPOINTMENT_ALTERNATIVE_ACCEPTED"`.
- Rechazar: `200`; `status == "REJECTED"`; `RejectedAt` no nulo; `RejectedBy == ids.PatientSubject`; los tres campos de propuesta siguen no nulos; evento `APPOINTMENT_ALTERNATIVE_REJECTED`.
- Vencido: mover `ProposalExpiresAt` al pasado después de proponer; `409 PROPOSAL_EXPIRED`; la cita queda `PENDING`; los tres campos de propuesta nulos; evento `APPOINTMENT_ALTERNATIVE_EXPIRED`; el `GET` posterior devuelve `PENDING`.

- [ ] **Step 2: Run tests to verify they fail**

Run:

```bash
TEST_DATABASE_URL=... dotnet test DentalConnect.slnx -c Release --filter FullyQualifiedName~AppointmentProposalApiTests
```

Expected: FAIL con `404`; las rutas no existen.

- [ ] **Step 3: Implement the routes and shared core**

En `MapAppointmentEndpoints`:

```csharp
endpoints.MapPost("/api/appointments/{appointmentId}/proposal/accept", AcceptProposalAsync).RequireAuthorization();
endpoints.MapPost("/api/appointments/{appointmentId}/proposal/reject", RejectProposalAsync).RequireAuthorization();
```

Implementar también `GetProposalAsync` (adelanto de Task 5): `TryGetPatientSubject`, `AsNoTracking().SingleOrDefaultAsync(a => a.Id == appointmentId && a.PatientSubject == subject)`, `404` si no existe y `AppointmentResponse(appointment, 200)` si existe. Sin efectos secundarios.

En `RespondToProposalAsync`:

1. `TryGetPatientSubject`; si falla, `401`.
2. Abrir transacción `Serializable`.
3. Cargar la cita con `SingleOrDefaultAsync(a => a.Id == appointmentId && a.PatientSubject == subject)`; si no existe, `404`.
4. Si `Status != "PROPOSED"`, `409 PROPOSAL_NOT_ACTIVE`.
5. Si `ProposedStartsAt`, `ProposedEndsAt` o `ProposalExpiresAt` son nulos, `409 PROPOSAL_NOT_ACTIVE`.
6. Si `IsExpired(now, ProposalExpiresAt.Value)`: limpiar los tres campos, `Status = "PENDING"`, encolar `APPOINTMENT_ALTERNATIVE_EXPIRED`, guardar, commit y `409 PROPOSAL_EXPIRED`.
7. Si `accept`: mover `StartsAt/EndsAt`, `Status = "CONFIRMED"`, limpiar los tres campos y encolar `APPOINTMENT_ALTERNATIVE_ACCEPTED`. La revalidación completa de disponibilidad se agrega en Task 4; en Task 3 solo se cubre el flujo de estado con un intervalo libre.
8. Si no: `Status = "REJECTED"`, `RejectedAt = now`, `RejectedBy = subject` y encolar `APPOINTMENT_ALTERNATIVE_REJECTED`.
9. `SaveChanges` + `Commit`; devolver `AppointmentResponse(appointment, 200)`.
10. En `catch` de `40001`, `23505` o `23P01`, rollback y `409 APPOINTMENT_CONFLICT`.

`QueueClinicEventAsync` busca el evento tracked por cita/tipo; si existe, limpia `ProcessedAt` y actualiza `CreatedAt`; si no, lo agrega. Usar `now` UTC.

Agregar `// NOTE (DENT-15): visualización in-app del aviso en HU-15.` junto al encolado de eventos y `// NOTE (DENT-31): barrido proactivo y aviso de vencimiento en TEC-07.` junto al guard de vencimiento.

- [ ] **Step 4: Run tests to verify they pass**

Run:

```bash
TEST_DATABASE_URL=... dotnet test DentalConnect.slnx -c Release --filter FullyQualifiedName~AppointmentProposalApiTests
```

Expected: los casos de Task 2 y Task 3 en verde.

- [ ] **Step 5: Commit**

```bash
git add DentalConnect/AppointmentEndpoints.cs DentalConnect.Tests/AppointmentProposalApiTests.cs
git commit -m "Sprint 7: aceptar o rechazar propuesta de horario (HU-14 · DENT-14)"
```

---

### Task 4: Revalidación de aceptación, conflictos y concurrencia

**Files:**
- Modify: `DentalConnect/AppointmentEndpoints.cs` (`RespondToProposalAsync`, rama `accept`)
- Modify: `DentalConnect.Tests/AppointmentProposalApiTests.cs`

**Interfaces:**
- Consumes: `RespondToProposalAsync` de Task 3.
- Produces: garantía de que aceptar no confirma un intervalo inválido y de que dos respuestas simultáneas dejan un solo estado.

- [ ] **Step 1: Write the failing tests**

Agregar:

```csharp
[PostgresFact] public async Task AcceptProposal_Occupied_Returns409AndKeepsProposed()
[PostgresFact] public async Task AcceptProposal_BlockOverlap_Returns409()
[PostgresFact] public async Task AcceptProposal_BreakOverlap_Returns409()
[PostgresFact] public async Task AcceptProposal_OutsideSchedule_Returns409()
[PostgresFact] public async Task AcceptProposal_DentistUnavailable_Returns409()
[PostgresFact] public async Task AcceptProposal_ServiceChanged_Returns409()
[PostgresFact] public async Task AcceptProposal_PatientConflict_Returns409()
[PostgresFact] public async Task AcceptProposal_Concurrent_OnlyOneConfirmed()
[PostgresFact] public async Task ExpireProposal_SecondCycle_ReusesExpiredEvent()
```

Aserciones:

- Ocupado: proponer; insertar una cita `CONFIRMED` del mismo dentista en el intervalo propuesto; aceptar → `409 SLOT_UNAVAILABLE`; la cita sigue `PROPOSED`; no hay evento `APPOINTMENT_ALTERNATIVE_ACCEPTED`.
- Bloqueo: proponer; agregar un `DentistScheduleBlock` que cubra el intervalo propuesto; aceptar → `409 SLOT_UNAVAILABLE`.
- Descanso: proponer; mover `BreakStartsAt/BreakEndsAt` del horario semanal para cubrir el intervalo propuesto; aceptar → `409 SLOT_UNAVAILABLE`.
- Fuera de jornada: proponer; mover `DentistWeeklySchedule.EndsAt` antes del inicio propuesto; aceptar → `409 SLOT_UNAVAILABLE`.
- Dentista inactivo: proponer; `Dentist.IsActive = false`; aceptar → `409 DENTIST_UNAVAILABLE`.
- Servicio cambiado: proponer; `DentalService.EstimatedDurationMinutes = 45`; aceptar → `409 SERVICE_CHANGED`.
- Conflicto de paciente: proponer; insertar una cita `CONFIRMED` del mismo `PatientSubject` en el intervalo con otro dentista; aceptar → `409 PATIENT_CONFLICT`.
- Concurrencia: dos `POST accept` simultáneos con el mismo subject; exactamente uno `200` y uno `409`; una cita `CONFIRMED`; un solo evento `APPOINTMENT_ALTERNATIVE_ACCEPTED`.
- Segundo ciclo de vencimiento: proponer → vencer → proponer de nuevo → vencer de nuevo; un solo evento `APPOINTMENT_ALTERNATIVE_EXPIRED`, con `ProcessedAt == null`; la cita queda `PENDING`.

Usar `UpdateAsync` para mutar filas después de proponer. El cleanup existente elimina citas y dentistas por consultorio.

- [ ] **Step 2: Run tests to verify they fail**

Run:

```bash
TEST_DATABASE_URL=... dotnet test DentalConnect.slnx -c Release --filter FullyQualifiedName~AppointmentProposalApiTests
```

Expected: FAIL en los casos nuevos.

- [ ] **Step 3: Complete the accept branch**

En `RespondToProposalAsync`, antes de mover el horario:

1. Cargar dentista con `WeeklySchedules`, `ScheduleBlocks` y `DentistServices.DentalService`; exigir `IsActive` y `ClinicId == appointment.ClinicId`; si no, `DENTIST_UNAVAILABLE`.
2. Cargar el servicio activo; verificar que el consultorio lo ofrece, que el dentista lo ofrece y que `ProposedEndsAt - ProposedStartsAt` coincide con `EstimatedDurationMinutes`; si no, `SERVICE_CHANGED`.
3. Verificar que el paciente no tenga otra cita `CONFIRMED` que se cruce con `[proposedStart, proposedEnd)`; si hay, `PATIENT_CONFLICT`.
4. Consultar citas `CONFIRMED` del dentista que se cruzan y generar slots con `PublicAvailabilityRules.Generate(day, 1, duration, dentist.WeeklySchedules, dentist.ScheduleBlocks, zone, now, confirmed)`.
5. Exigir un slot con `StartsAt == proposedStart && EndsAt == proposedEnd`; si no, `SLOT_UNAVAILABLE`.

Solo después de esas validaciones mover `StartsAt/EndsAt`, cambiar a `CONFIRMED`, limpiar la propuesta y encolar el evento. Mantener el `catch` de `40001`, `23505` y `23P01` como `APPOINTMENT_CONFLICT`.

- [ ] **Step 4: Run tests to verify they pass**

Run:

```bash
TEST_DATABASE_URL=... dotnet test DentalConnect.slnx -c Release --filter FullyQualifiedName~AppointmentProposalApiTests
```

Expected: todos los casos de Task 2, 3 y 4 en verde.

- [ ] **Step 5: Commit**

```bash
git add DentalConnect/AppointmentEndpoints.cs DentalConnect.Tests/AppointmentProposalApiTests.cs
git commit -m "Sprint 7: cubrir conflictos y concurrencia de respuesta (HU-14 · DENT-14)"
```

---

### Task 5: Contrato de lectura de la propuesta

**Files:**
- Modify: `DentalConnect.Tests/AppointmentProposalApiTests.cs`

**Interfaces:**
- Consumes: `GetProposalAsync` implementado en Task 3.
- Produces: pruebas de contrato camelCase de `GET /api/appointments/{appointmentId}/proposal`.

- [ ] **Step 1: Write the contract tests**

Agregar:

```csharp
[PostgresFact] public async Task GetProposal_ReturnsBothSchedules()
[PostgresFact] public async Task GetProposal_AfterReject_ReturnsRejected()
```

El caso `GetProposal_ClinicAdminToken_Returns404` ya se escribió en Task 2; debe seguir verde.

Aserciones:

- Antes de la propuesta: `status == "PENDING"` y los tres campos propuestos nulos.
- Después de proponer: `status == "PROPOSED"`, `startsAt` original, `proposedStartsAt` alternativo y `proposalExpiresAt` no nulo.
- Después de rechazar: `status == "REJECTED"`, `startsAt` original y `proposedStartsAt` conservado.
- Token de consultorio: cubierto por `GetProposal_ClinicAdminToken_Returns404` de Task 2; `NewClient("CLINIC_ADMIN", ids.ClinicId)` → `404`, igual que otro paciente.

- [ ] **Step 2: Run the contract tests**

Run:

```bash
TEST_DATABASE_URL=... dotnet test DentalConnect.slnx -c Release --filter FullyQualifiedName~AppointmentProposalApiTests
```

Expected: PASS. Ruling: no hay RED posible porque la ruta se adelantó a Task 3 para no dejar en rojo las pruebas 401/404 de Task 2. La autorización ya se probó en Task 2; estas pruebas fijan la forma camelCase y que `GET` no tenga efectos secundarios.

- [ ] **Step 3: Verify no side effects**

La implementación vive en Task 3 (`GetProposalAsync`); no duplicar la ruta. Confirmar que el `GET` no escribe estado: el vencimiento se resuelve al aceptar/rechazar.

- [ ] **Step 4: Run tests to verify they pass**

Run:

```bash
TEST_DATABASE_URL=... dotnet test DentalConnect.slnx -c Release --filter FullyQualifiedName~AppointmentProposalApiTests
```

Expected: verde.

- [ ] **Step 5: Commit**

```bash
git add DentalConnect/AppointmentEndpoints.cs DentalConnect.Tests/AppointmentProposalApiTests.cs
git commit -m "Sprint 7: consultar propuesta de horario del paciente (HU-14 · DENT-14)"
```

---

### Task 6: Verificación, conciliación y guardas

**Files:**
- Modify: `specs/hu-14-aceptar-rechazar-horario-alternativo/spec.md`
- Modify: `specs/hu-14-aceptar-rechazar-horario-alternativo/task.md`
- Modify: `specs/hu-13-proponer-horario-alternativo/spec.md:20,171,175`
- Modify: `specs/hu-13-proponer-horario-alternativo/plan.md:27`
- Modify: `specs/hu-13-proponer-horario-alternativo/task.md:38`
- Modify: `DentalConnect/AppointmentEndpoints.cs:284` (retirar `NOTE (DENT-14)`)

**Interfaces:**
- Consumes: todo lo anterior.
- Produces: evidencia real y cierre de trazabilidad.

- [ ] **Step 1: Remove the consumed NOTE**

Run:

```bash
grep -rn "NOTE (DENT-14)" DentalConnect DentalConnect.Domain DentalConnect.Infrastructure DentalConnect.Tests
```

Retirar el `NOTE (DENT-14)` de `ProposeAsync`. Actualizar las cinco referencias de HU-13: `spec.md:20` (quitar el NOTE y decir que HU-14 la cubre), `spec.md:171` (quitar el NOTE de la lista), `spec.md:175` (retirar el límite de aceptar/rechazar), `plan.md:27` (quitar el NOTE de Global Constraints) y `task.md:38` (retirar la fila de pendientes). Verificar con:

```bash
grep -rn "NOTE (DENT-14)" specs/hu-13-proponer-horario-alternativo
```

Expected: sin coincidencias. No tocar `NOTE (DENT-15)` ni `NOTE (DENT-31)`.

- [ ] **Step 2: Run the full build**

Run:

```bash
dotnet build DentalConnect.slnx -c Release
```

Expected: 0 errores, 0 advertencias.

- [ ] **Step 3: Run the full suite with PostgreSQL**

Run:

```bash
TEST_DATABASE_URL=... dotnet test DentalConnect.slnx -c Release
```

Expected: N/N en verde. Registrar el conteo real en `spec.md` y `task.md`; no inventarlo. Sin `TEST_DATABASE_URL`, las pruebas API se omiten y se reporta así.

- [ ] **Step 4: Confirm no schema change**

Run:

```bash
git diff --stat main -- DentalConnect.Infrastructure/Persistence/Migrations docs/base-datos/migraciones.md
```

Expected: sin cambios. No ejecutar ciclo de migraciones.

- [ ] **Step 5: Run the security and traceability gates**

- `security-reviewer` sobre el diff contra `main`: IDOR, ownership, PII, 401/404, eventos y transacciones.
- `traceability-clerk` sobre Jira ↔ spec ↔ código ↔ test: `DENT-14`, nombres de pruebas, `NOTE` vigentes y pendientes con dueño.
- Corregir los gaps y repetir.

- [ ] **Step 6: Update evidence and spec state**

Cambiar `Estado` a `🚧 Borrador con evidencia` y anotar los conteos reales, los commits y el resultado de las guardas. No usar `✅ Aprobado` sin revisor indicado por el usuario.

- [ ] **Step 7: Update Jira**

Consultar las transiciones de `DENT-14` con `listJiraIssueTransitions`; moverlo al estado que corresponda al cierre (no inventar nombres) y agregar un comentario con el enlace del PR usando `addOrEditJiraIssueComment`. Si el PR aún no existe, dejar el enlace pendiente y no marcar el issue como terminado.

Título del PR: `Sprint 7: aceptar o rechazar un horario alternativo (HU-14 · DENT-14) (Unit N · Api N)`, con los conteos reales de la suite.

- [ ] **Step 8: Commit**

```bash
git add specs/hu-14-aceptar-rechazar-horario-alternativo specs/hu-13-proponer-horario-alternativo DentalConnect/AppointmentEndpoints.cs
git commit -m "Sprint 7: cerrar evidencia de respuesta a propuesta (HU-14 · DENT-14)"
```

---

## Execution Handoff

Plan completo. Ejecución recomendada: nativa y secuencial, porque el cambio extiende un solo módulo y comparte helpers de prueba. `slice-scaffolder` no aplica: no hay entidad ni módulo nuevo. Usar `security-reviewer` antes del push y `traceability-clerk` al cierre.

Dependencias externas:

- Vencimiento proactivo y aviso al consultorio: TEC-07/DENT-31.
- Visualización in-app de notificaciones: HU-15/DENT-15 (Sprint 08); email/SMS/push fuera de alcance.
- Listado e historial del paciente: HU-16/DENT-16.
- UI de paciente: PR separado en `DentalConnect-front` con `DENT-14`.

Riesgo principal: sin `TEST_DATABASE_URL`, la suite API se omite; la evidencia debe decir "omitida", no "verde". Segundo riesgo: el barrido de TEC-07 no existe todavía; si el paciente nunca responde, la propuesta vencida sigue `PROPOSED` hasta que se implemente.
