# HU-13 Proponer un horario alternativo Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Permitir que un `CLINIC_ADMIN` proponga un horario alternativo a una solicitud `PENDING`, conservando el horario original y sin reservar el intervalo.

**Architecture:** La propuesta se guarda en la misma fila `Appointment` (`ProposedStartsAt`, `ProposedEndsAt`, `ProposalExpiresAt`) y mueve el estado a `PROPOSED`. El endpoint valida disponibilidad con `PublicAvailabilityRules.Generate`, persiste un evento outbox y no toca las restricciones de agenda porque la propuesta no reserva.

**Tech Stack:** .NET 10, ASP.NET Core Minimal APIs, EF Core 10, PostgreSQL 16, xUnit 2.9, WebApplicationFactory, `TEST_DATABASE_URL`.

**Spec:** `specs/hu-13-proponer-horario-alternativo/spec.md`

## Global Constraints

- Estado `PROPOSED` en UPPER_SNAKE; la BD debe aceptarlo en `CK_Appointment_Status`.
- `StartsAt`/`EndsAt` originales son inmutables.
- La propuesta no reserva: no tocar `CreateAsync`, `ConfirmAsync`, `RejectAsync`, `PublicAvailabilityEndpoints` ni las exclusiones `WHERE Status = 'CONFIRMED'`.
- Una sola propuesta activa por solicitud.
- `ProposalExpiresAt = min(now + 24 h, ProposedStartsAt)`.
- Solo `CLINIC_ADMIN` con claim `clinic_id` o `custom:clinic_id`; otro consultorio → 404.
- Zona del piloto `America/Mexico_City`; persistir UTC.
- Evento outbox `APPOINTMENT_ALTERNATIVE_PROPOSED`, único por cita/tipo; reutilizar si existe.
- No agregar paquetes NuGet.
- No editar migraciones ya aplicadas.
- Migración manual: el snapshot EF está desincronizado.
- No usar `dotnet ef database update` sin destino: falla por `PendingModelChangesWarning`. Usar destinos explícitos o `database-migrations.yml`.
- Sin `TODO`; usar `NOTE (DENT-14)`, `NOTE (DENT-15)` y `NOTE (DENT-31)`.
- Build Release con 0 errores y 0 advertencias.

## Review Focus

1. `proposedStartsAt` exactamente igual a `now` o un instante anterior → `409 PAST_DATE`, sin persistir.
2. Propuesta que cruza descanso, bloqueo o cita `CONFIRMED` → `409 SLOT_UNAVAILABLE`.
3. Dos propuestas simultáneas sobre la misma solicitud → una `200`, una `409`; una sola fila `PROPOSED`.
4. Solicitud original ya pasada pero alternativa futura → se permite; la vigencia se calcula contra el inicio propuesto.
5. Reintento tras vencimiento (TEC-07 devuelve a `PENDING`) → reutiliza el evento outbox sin violar el índice único.

## File Structure

| Archivo | Responsabilidad |
|---|---|
| `DentalConnect.Domain/Entities/Entities.cs` | Campos de propuesta en `Appointment`. |
| `DentalConnect.Domain/Entities/AppointmentScheduling.cs` | Reglas puras de vigencia y pasado. |
| `DentalConnect.Infrastructure/Persistence/Migrations/20261110120000_Sprint7AppointmentProposal.cs` | Columnas, CHECK de estado y CHECK de coherencia. |
| `DentalConnect/AppointmentEndpoints.cs` | Request, endpoint, estado, proyecciones y evento. |
| `docs/base-datos/migraciones.md` | Evidencia de la migración. |
| `DentalConnect.Tests/AppointmentProposalRulesTests.cs` | Reglas puras. |
| `DentalConnect.Tests/TestAuthHandler.cs` | Autenticación de prueba para API. |
| `DentalConnect.Tests/PostgresApiFixture.cs` | Host API + migraciones + PostgreSQL de CI. |
| `DentalConnect.Tests/AppointmentProposalApiTests.cs` | Flujos HTTP, errores y concurrencia. |
| `DentalConnect.Tests/PublicAvailabilityRulesTests.cs` | Caso `PROPOSED` no bloqueante. |

---

### Task 1: Dominio y reglas puras

**Files:**
- Modify: `DentalConnect.Domain/Entities/Entities.cs:121-144`
- Create: `DentalConnect.Domain/Entities/AppointmentScheduling.cs`
- Test: `DentalConnect.Tests/AppointmentProposalRulesTests.cs`

**Interfaces:**
- Consumes: nada.
- Produces:
  - `Appointment.ProposedStartsAt`, `Appointment.ProposedEndsAt`, `Appointment.ProposalExpiresAt` (`DateTimeOffset?`).
  - `AppointmentProposalRules.ExpiresAt(DateTimeOffset now, DateTimeOffset proposedStart) -> DateTimeOffset`.
  - `AppointmentProposalRules.IsPast(DateTimeOffset now, DateTimeOffset start) -> bool`.

- [ ] **Step 1: Write the failing tests**

`AppointmentProposalRulesTests.cs`:

```csharp
using DentalConnect.Domain.Entities;

namespace DentalConnect.Tests;

public class AppointmentProposalRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ExpiresAt_Uses24HoursWhenProposedStartIsLater()
    {
        var proposed = Now.AddHours(30);
        Assert.Equal(Now.AddHours(24), AppointmentProposalRules.ExpiresAt(Now, proposed));
    }

    [Fact]
    public void ExpiresAt_UsesProposedStartWhenSooner()
    {
        var proposed = Now.AddHours(2);
        Assert.Equal(proposed, AppointmentProposalRules.ExpiresAt(Now, proposed));
    }

    [Fact]
    public void IsPast_RejectsNowAndEarlier()
    {
        Assert.True(AppointmentProposalRules.IsPast(Now, Now));
        Assert.True(AppointmentProposalRules.IsPast(Now, Now.AddSeconds(-1)));
        Assert.False(AppointmentProposalRules.IsPast(Now, Now.AddSeconds(1)));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:
```bash
dotnet test DentalConnect.slnx -c Release --filter FullyQualifiedName~AppointmentProposalRulesTests
```
Expected: FAIL de compilación porque `AppointmentProposalRules` no existe.

- [ ] **Step 3: Implement the minimum code**

En `Entities.cs`, dentro de `Appointment`, agregar:

```csharp
public DateTimeOffset? ProposedStartsAt { get; set; }
public DateTimeOffset? ProposedEndsAt { get; set; }
public DateTimeOffset? ProposalExpiresAt { get; set; }
```

En `AppointmentScheduling.cs`:

```csharp
namespace DentalConnect.Domain.Entities;

public static class AppointmentProposalRules
{
    public static DateTimeOffset ExpiresAt(DateTimeOffset now, DateTimeOffset proposedStart) =>
        proposedStart < now.AddHours(24) ? proposedStart : now.AddHours(24);

    public static bool IsPast(DateTimeOffset now, DateTimeOffset start) => start <= now;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run:
```bash
dotnet test DentalConnect.slnx -c Release --filter FullyQualifiedName~AppointmentProposalRulesTests
```
Expected: 3/3 PASS.

- [ ] **Step 5: Commit**

```bash
git add DentalConnect.Domain/Entities/Entities.cs DentalConnect.Domain/Entities/AppointmentScheduling.cs DentalConnect.Tests/AppointmentProposalRulesTests.cs
git commit -m "Sprint 7: agregar reglas de propuesta de horario (HU-13 · DENT-13)"
```

---

### Task 2: Migración manual y evidencia

**Files:**
- Create: `DentalConnect.Infrastructure/Persistence/Migrations/20261110120000_Sprint7AppointmentProposal.cs`
- Modify: `docs/base-datos/migraciones.md`

**Interfaces:**
- Consumes: campos de Task 1.
- Produces: columnas `ProposedStartsAt`, `ProposedEndsAt`, `ProposalExpiresAt`; `CK_Appointment_Status` con `PROPOSED`; `CK_Appointment_Proposal`.

- [ ] **Step 1: Write the migration**

Usar el patrón de `Sprint6ClinicAppointmentReview`:

```csharp
[DbContext(typeof(DentalConnectDbContext))]
[Migration("20261110120000_Sprint7AppointmentProposal")]
public sealed class Sprint7AppointmentProposal : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "Appointment" DROP CONSTRAINT "CK_Appointment_Status";
            ALTER TABLE "Appointment" ADD CONSTRAINT "CK_Appointment_Status"
                CHECK ("Status" IN ('PENDING','CONFIRMED','REJECTED','CANCELLED','PROPOSED'));
            ALTER TABLE "Appointment"
                ADD COLUMN "ProposedStartsAt" timestamp with time zone NULL,
                ADD COLUMN "ProposedEndsAt" timestamp with time zone NULL,
                ADD COLUMN "ProposalExpiresAt" timestamp with time zone NULL;
            ALTER TABLE "Appointment" ADD CONSTRAINT "CK_Appointment_Proposal" CHECK (
                ("ProposedStartsAt" IS NULL AND "ProposedEndsAt" IS NULL AND "ProposalExpiresAt" IS NULL)
                OR ("ProposedStartsAt" IS NOT NULL AND "ProposedEndsAt" IS NOT NULL
                    AND "ProposalExpiresAt" IS NOT NULL AND "ProposedStartsAt" < "ProposedEndsAt"));
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE "Appointment" SET "Status" = 'PENDING' WHERE "Status" = 'PROPOSED';
            ALTER TABLE "Appointment" DROP CONSTRAINT IF EXISTS "CK_Appointment_Proposal";
            ALTER TABLE "Appointment"
                DROP COLUMN IF EXISTS "ProposedStartsAt",
                DROP COLUMN IF EXISTS "ProposedEndsAt",
                DROP COLUMN IF EXISTS "ProposalExpiresAt";
            ALTER TABLE "Appointment" DROP CONSTRAINT "CK_Appointment_Status";
            ALTER TABLE "Appointment" ADD CONSTRAINT "CK_Appointment_Status"
                CHECK ("Status" IN ('PENDING','CONFIRMED','REJECTED','CANCELLED'));
            """);
    }
}
```

- [ ] **Step 2: Add the migration row**

En `docs/base-datos/migraciones.md`, agregar una tabla `Historial de migraciones` con las migraciones existentes y la nueva:

```markdown
| Migración | Fecha | Estado |
|---|---|---|
| `20261015060846_InitialCreate` | 15-Oct-2026 | Aplicada |
| `20261020120000_Sprint3DentistScheduling` | 20-Oct-2026 | Aplicada |
| `20261109000000_Sprint5Appointments` | 09-Nov-2026 | Aplicada |
| `20261109010000_Sprint5DemoDentistServices` | 09-Nov-2026 | Aplicada |
| `20261109020000_Sprint5DemoDentistSchedule` | 09-Nov-2026 | Aplicada |
| `20261109030000_Sprint6ClinicAppointmentReview` | 09-Nov-2026 | Aplicada |
| `20261110120000_Sprint7AppointmentProposal` | 10-Nov-2026 | Pendiente de aplicar |
```

- [ ] **Step 3: Verify the migration cycle**

Run:
```bash
dotnet ef database update 20261110120000_Sprint7AppointmentProposal --project DentalConnect.Infrastructure --startup-project DentalConnect
dotnet ef database update 20261109030000_Sprint6ClinicAppointmentReview --project DentalConnect.Infrastructure --startup-project DentalConnect
dotnet ef database update 20261110120000_Sprint7AppointmentProposal --project DentalConnect.Infrastructure --startup-project DentalConnect
```
Expected: apply, revert and re-apply without errors. En CI, usar `database-migrations.yml` (pasa destinos explícitos).

- [ ] **Step 4: Commit**

```bash
git add DentalConnect.Infrastructure/Persistence/Migrations/20261110120000_Sprint7AppointmentProposal.cs docs/base-datos/migraciones.md
git commit -m "Sprint 7: migrar estado y columnas de propuesta (HU-13 · DENT-13)"
```

---

### Task 3: Infraestructura de pruebas HTTP y datos deterministas

**Files:**
- Create: `DentalConnect.Tests/TestAuthHandler.cs`
- Create: `DentalConnect.Tests/PostgresApiFixture.cs`
- Create: `DentalConnect.Tests/AppointmentProposalApiTests.cs`

**Interfaces:**
- Consumes: reglas de Task 1; migración de Task 2.
- Produces:
  - `TestAuthHandler.Scheme = "Test"`; headers `X-Test-Sub`, `X-Test-Role`, `X-Test-Clinic`.
  - `PostgresApiFixture.ConnectionString`, `Available`, `Factory`, `CreateScope()`.
  - `SeedAsync(...)` y `SeedIds` para las pruebas HTTP.

- [ ] **Step 1: Write the failing auth/status tests**

En `AppointmentProposalApiTests.cs`, crear la clase con `[Collection("Postgres API")]` y estos casos:

```csharp
[Fact] public async Task Propose_WithoutToken_Returns401()
[Fact] public async Task Propose_WithoutClinicAdminRole_Returns403()
[Fact] public async Task Propose_AppointmentFromAnotherClinic_Returns404()
[Fact] public async Task Propose_InvalidStatus_Returns409()
[Fact] public async Task Propose_PastAlternative_Returns409()
[Fact] public async Task Propose_SameStartAsOriginal_Returns409()
```

Aserciones mínimas:

- 401: `HttpStatusCode.Unauthorized`.
- 403: token con rol `PATIENT` o sin `clinic_id`; `HttpStatusCode.Forbidden`.
- 404: cita de clínica B con token de clínica A.
- 409: `error.code` igual a `INVALID_STATUS`, `PAST_DATE` y `PROPOSAL_SAME_AS_ORIGINAL`.
- Para `INVALID_STATUS`, mutar la cita a `CONFIRMED` después del seed.
- Si `TEST_DATABASE_URL` no existe, cada test retorna sin fallar (patrón de `AppointmentConcurrencyTests`).

- [ ] **Step 2: Run tests to verify they fail**

Run:
```bash
dotnet test DentalConnect.slnx -c Release --filter FullyQualifiedName~AppointmentProposalApiTests
```
Expected: FAIL; la ruta no existe o el fixture no compila.

- [ ] **Step 3: Implement test infrastructure**

`TestAuthHandler`:

- `public const string Scheme = "Test";`
- Si falta `X-Test-Sub`, devolver `AuthenticateResult.NoResult()`.
- Crear `new ClaimsIdentity(claims, TestAuthHandler.Scheme, "sub", "cognito:groups")` con claims `sub`, `cognito:groups`, `clinic_id`; el `authenticationType` no puede ser nulo o la identidad no queda autenticada.
- Devolver `AuthenticateResult.Success`.

`PostgresApiFixture`:

- Lee `TEST_DATABASE_URL`; `Available = !string.IsNullOrWhiteSpace(...)`.
- Crea `WebApplicationFactory<Program>` con `UseEnvironment("Testing")`, `UseSetting("COGNITO_USER_POOL_ID", "us-east-1_test")`, `UseSetting("COGNITO_CLIENT_ID", "test-client")` y `UseSetting("ConnectionStrings:DefaultConnection", connectionString)`.
- En `ConfigureTestServices`: fija `TestAuthHandler` como esquema default:
  `services.AddAuthentication(TestAuthHandler.Scheme).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { });`
- Reemplaza el `DbContext` de Program: remover `DbContextOptions<DentalConnectDbContext>` e `IDbContextOptionsConfiguration<DentalConnectDbContext>`, y volver a registrar `AddDbContext` con `UseNpgsql(connectionString)` y `ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))`.
- `InitializeAsync`: si `Available`, crear scope y `db.Database.MigrateAsync()`.
- `CreateScope()`: devuelve `Factory.Services.CreateScope()`; el test resuelve `DentalConnectDbContext` del scope y lo libera con `using`.
- `DisposeAsync`: `Factory.Dispose()`.
- Agregar `[CollectionDefinition("Postgres API")] public sealed class PostgresApiCollection : ICollectionFixture<PostgresApiFixture>;` y hacer que el fixture implemente `IAsyncLifetime`.

- [ ] **Step 4: Add the deterministic seed helper**

En `AppointmentProposalApiTests.cs`, agregar:

```csharp
private sealed record SeedIds(
    string ClinicId, string DentistId, string ServiceId, string AppointmentId,
    DateTimeOffset OriginalStartsAt, DateTimeOffset ProposedStartsAt, DateOnly ProposedLocalDate);
```

`SeedAsync(DentalConnectDbContext db, bool dentistActive = true, int durationMinutes = 30,
bool withConfirmedOverlap = false, bool withBlockOverlap = false, bool withOtherClinic = false)` debe:

- usar `CuidGenerator.NewId()` para `Clinic`, `DentalService`, `ClinicService`, `Dentist`, `DentistService`, `DentistWeeklySchedule` y `Appointment`;
- usar la zona `America/Mexico_City` y `localDate = hoy local + 7 días`;
- crear jornada `09:00–12:00` para el `DayOfWeek` de `localDate`;
- dejar la cita original en `localDate.AddDays(2)`, con duración de `durationMinutes`;
- devolver `ProposedStartsAt` = `localDate` a las `10:00` locales convertido a UTC;
- asignar a cada cita sembrada `Folio = "DC-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()`, `PatientSubject = "test-" + Guid.NewGuid().ToString("N")` e `IdempotencyKey = Guid.NewGuid().ToString("N")` para no violar los índices únicos;
- si `withConfirmedOverlap`, crear otra cita `CONFIRMED` a las `10:00` locales con sus propios `Folio`/`PatientSubject`/`IdempotencyKey`;
- si `withBlockOverlap`, crear un `DentistScheduleBlock` de `10:00–10:30` locales;
- si `withOtherClinic`, crear una clínica B con su propio dentista y cita, con datos únicos;
- usar el mismo `ClinicId`/`DentistId`/`ServiceId` en las claves foráneas;
- limpiar las filas creadas en el `finally` de cada prueba.

- [ ] **Step 5: Run tests to verify they fail for the right reason**

Run:
```bash
dotnet test DentalConnect.slnx -c Release --filter FullyQualifiedName~AppointmentProposalApiTests
```
Expected: FAIL con `404` porque la ruta aún no existe. No debe fallar por compilación del fixture ni del helper.

- [ ] **Step 6: Do not commit yet**

La Fase A no se commitea sola (skill `vertical-slice`). Task 4 implementa el endpoint completo y commitea ambos.

---

### Task 4: Endpoint completo: permisos, disponibilidad y persistencia

**Files:**
- Modify: `DentalConnect/AppointmentEndpoints.cs:18-29,235-253` y agregar `ProposeAsync` (no tocar la lógica de `CreateAsync`/`ConfirmAsync`/`RejectAsync`)
- Modify: `DentalConnect.Tests/AppointmentProposalApiTests.cs` (creado en Task 3)
- Incluir en el commit: `DentalConnect.Tests/TestAuthHandler.cs` y `DentalConnect.Tests/PostgresApiFixture.cs` (creados en Task 3)

**Interfaces:**
- Consumes: `AppointmentProposalRules.ExpiresAt`, `PublicAvailabilityRules.Generate`, `ClinicNotificationEvent`.
- Produces: transición `PENDING -> PROPOSED`, `ProposedStartsAt/EndsAt`, `ProposalExpiresAt`, evento `APPOINTMENT_ALTERNATIVE_PROPOSED`.

- [ ] **Step 1: Write the failing success/conflict tests**

Agregar:

```csharp
[Fact] public async Task Propose_AvailableAlternative_ReturnsProposedWithBothSchedulesAndEvent()
[Fact] public async Task Propose_SecondActiveProposal_Returns409()
[Fact] public async Task Propose_OccupiedInterval_Returns409AndDoesNotChangeAppointment()
[Fact] public async Task Propose_BreakOrBlockOverlap_Returns409()
[Fact] public async Task Propose_OutsideSchedule_Returns409()
[Fact] public async Task Propose_InactiveDentist_Returns409DentistUnavailable()
[Fact] public async Task Propose_ServiceDurationChanged_Returns409ServiceChanged()
[Fact] public async Task Propose_OriginalInPastButAlternativeFuture_Returns200()
```

Aserciones:

- Éxito: `200`; `data.status == "PROPOSED"`; `data.startsAt` igual al original; `data.proposedStartsAt` igual al enviado; `data.proposalExpiresAt` dentro de `[now+24h-5s, now+24h+5s]` (el seed propone a +7 días); una fila `ClinicNotificationEvent` con `EventType == "APPOINTMENT_ALTERNATIVE_PROPOSED"`.
- Segunda propuesta: primero `200`; segunda `409 PROPOSAL_ALREADY_ACTIVE`; una sola fila `PROPOSED` y un solo evento.
- Ocupado: `409 SLOT_UNAVAILABLE`; la cita sigue `PENDING`; no hay evento.
- Bloqueo: `409 SLOT_UNAVAILABLE`.
- Fuera de jornada: `409 SLOT_UNAVAILABLE`.
- Dentista inactivo: `409 DENTIST_UNAVAILABLE`.
- Duración cambiada: actualizar `DentalService.EstimatedDurationMinutes` a `durationMinutes + 15` después del seed; `409 SERVICE_CHANGED`.
- Original pasado + alternativa futura: `200 PROPOSED`. El test mueve `StartsAt/EndsAt` de la cita al pasado después del seed.
- Fuera de jornada: usar `SeedIds.ProposedLocalDate` para calcular las `20:00` locales.
- Estado inválido, dentista inactivo y duración cambiada: mutar la fila/servicio después del seed antes de llamar al endpoint.

- [ ] **Step 2: Run tests to verify they fail**

Run:
```bash
dotnet test DentalConnect.slnx -c Release --filter FullyQualifiedName~AppointmentProposalApiTests
```
Expected: FAIL en los casos nuevos.

- [ ] **Step 3: Implement the endpoint**

En `AppointmentEndpoints.cs`:

```csharp
public sealed record ProposeAlternativeRequest(DateTimeOffset ProposedStartsAt);
```

Agregar `"PROPOSED"` a `AppointmentStatuses`, mapear:

```csharp
endpoints.MapPost("/api/appointments/{appointmentId}/propose", ProposeAsync).RequireAuthorization();
```

`ProposeAsync`:

1. `TryGetClinicContext`; si falla, `Results.Forbid()`.
2. Si `body is null || body.ProposedStartsAt == default`, `400 VALIDATION_ERROR` (patrón de `CreateAsync`).
3. Abrir transacción `Serializable`.
4. Cargar la cita con `SingleOrDefaultAsync(a => a.Id == appointmentId && a.ClinicId == clinicId)`; si no existe, `404`.
5. Si `Status == "PROPOSED"` → `409 PROPOSAL_ALREADY_ACTIVE`; si `Status != "PENDING"` → `409 INVALID_STATUS`.
6. Convertir a UTC; si `AppointmentProposalRules.IsPast(now, start)` → `409 PAST_DATE`; si `start == appointment.StartsAt` → `409 PROPOSAL_SAME_AS_ORIGINAL`.
7. Cargar el dentista con `WeeklySchedules`, `ScheduleBlocks` y `DentistServices.DentalService`; exigir `IsActive` y `ClinicId == clinicId`; si no, `409 DENTIST_UNAVAILABLE`.
8. Cargar el servicio activo y verificar que el dentista lo ofrece, que el consultorio lo ofrece y que `appointment.EndsAt - appointment.StartsAt` coincide con `EstimatedDurationMinutes`; si no, `409 SERVICE_CHANGED`.
9. Calcular `end = start.AddMinutes(duration)`.
10. Convertir `start` a la zona del piloto y obtener `day`.
11. Consultar citas `CONFIRMED` del dentista que se cruzan con `[start, end)`.
12. `PublicAvailabilityRules.Generate(day, 1, duration, dentist.WeeklySchedules, dentist.ScheduleBlocks, zone, now, confirmed)`.
13. Exigir que exista un slot con `StartsAt == start && EndsAt == end`; si no, `409 SLOT_UNAVAILABLE`.
14. Asignar `Status = "PROPOSED"`, `ProposedStartsAt = start`, `ProposedEndsAt = end`, `ProposalExpiresAt = AppointmentProposalRules.ExpiresAt(now, start)`. Extender el helper `AppointmentResponse` con los tres campos para que la prueba de éxito pueda verificarlos.
15. Buscar el evento **tracked** (`SingleOrDefaultAsync`, sin `AsNoTracking`) por `AppointmentId` y `EventType == "APPOINTMENT_ALTERNATIVE_PROPOSED"`; si existe, `ProcessedAt = null` y `CreatedAt = now`; si no, agregar `new ClinicNotificationEvent { AppointmentId = appointment.Id, EventType = "APPOINTMENT_ALTERNATIVE_PROPOSED" }`.
16. `SaveChangesAsync` + `CommitAsync`.
17. En `catch` de `40001`/`23505`, rollback y `409 APPOINTMENT_CONFLICT`.

Usar `TimeZoneInfo.FindSystemTimeZoneById(PublicAvailabilityRules.PilotTimeZoneId)` y el helper `SqlState` existente.

- [ ] **Step 4: Run tests to verify they pass**

Run:
```bash
dotnet test DentalConnect.slnx -c Release --filter FullyQualifiedName~AppointmentProposalApiTests
```
Expected: todos los casos de Task 3 y Task 4 en verde.

- [ ] **Step 5: Commit**

```bash
git add DentalConnect/AppointmentEndpoints.cs DentalConnect.Tests/TestAuthHandler.cs DentalConnect.Tests/PostgresApiFixture.cs DentalConnect.Tests/AppointmentProposalApiTests.cs
git commit -m "Sprint 7: proponer horario alternativo con permisos y disponibilidad (HU-13 · DENT-13)"
```

---

### Task 5: Proyecciones de listado y detalle

**Files:**
- Modify: `DentalConnect/AppointmentEndpoints.cs:104-166`
- Modify: `DentalConnect.Tests/AppointmentProposalApiTests.cs`
- Modify: `DentalConnect.Tests/PublicAvailabilityRulesTests.cs`

**Interfaces:**
- Consumes: campos persistidos de Task 4.
- Produces: `proposedStartsAt`, `proposedEndsAt`, `proposalExpiresAt` en listado y detalle; `status=PROPOSED` aceptado.

- [ ] **Step 1: Write the failing projection tests**

En `AppointmentProposalApiTests.cs`:

```csharp
[Fact] public async Task List_WithProposedStatus_ReturnsProposalFields()
[Fact] public async Task Detail_ReturnsOriginalAndProposedSchedules()
```

En `PublicAvailabilityRulesTests.cs`:

```csharp
[Fact] public void ProposedStatusDoesNotBlockAvailability()
```

Aserciones:

- Listado: `GET /api/appointments?status=PROPOSED` devuelve la cita con los tres campos.
- Detalle: `startsAt` original, `proposedStartsAt` alternativo, `proposalExpiresAt` no nulo.
- Regla pura: un `Appointment` con `Status = "PROPOSED"` no elimina slots de `PublicAvailabilityRules.Generate`.

- [ ] **Step 2: Run tests to verify they fail**

Run:
```bash
dotnet test DentalConnect.slnx -c Release --filter FullyQualifiedName~AppointmentProposalApiTests
dotnet test DentalConnect.slnx -c Release --filter FullyQualifiedName~ProposedStatusDoesNotBlockAvailability
```
Expected: FAIL por campos ausentes.

- [ ] **Step 3: Implement the projections**

- En la proyección de listado agregar los tres campos.
- En la proyección de detalle agregar los tres campos.
- Mantener `startsAt`/`endsAt` como originales.
- No cambiar el orden ni los filtros de fecha existentes.
- El helper `AppointmentResponse` ya se extendió en Task 4; no duplicar el cambio.

- [ ] **Step 4: Run tests to verify they pass**

Run:
```bash
dotnet test DentalConnect.slnx -c Release --filter FullyQualifiedName~AppointmentProposalApiTests
dotnet test DentalConnect.slnx -c Release --filter FullyQualifiedName~PublicAvailabilityRulesTests
```
Expected: verde.

- [ ] **Step 5: Commit**

```bash
git add DentalConnect/AppointmentEndpoints.cs DentalConnect.Tests/AppointmentProposalApiTests.cs DentalConnect.Tests/PublicAvailabilityRulesTests.cs
git commit -m "Sprint 7: exponer ambos horarios de la propuesta (HU-13 · DENT-13)"
```

---

### Task 6: Concurrencia y reutilización del evento

**Files:**
- Modify: `DentalConnect.Tests/AppointmentProposalApiTests.cs`
- Modify: `DentalConnect/AppointmentEndpoints.cs` solo si la prueba revela una carrera.

**Interfaces:**
- Consumes: endpoint de Task 4.
- Produces: garantía de una sola propuesta activa y un solo evento por cita.

- [ ] **Step 1: Write the failing concurrency/event tests**

Agregar:

```csharp
[Fact] public async Task Propose_ConcurrentRequests_OnlyOneProposed()
[Fact] public async Task Propose_ExpiredEvent_IsReused()
```

Aserciones:

- Concurrencia: dos `POST` simultáneos; exactamente uno `200` y uno `409`; en BD una sola cita `PROPOSED`; un solo evento.
- Evento reutilizado: marcar el evento como procesado y la cita como `PENDING` (simula TEC-07), proponer de nuevo; mismo `Id` de evento, `ProcessedAt == null`.

- [ ] **Step 2: Run tests to verify they pass or reveal a race**

Run:
```bash
dotnet test DentalConnect.slnx -c Release --filter FullyQualifiedName~AppointmentProposalApiTests
```
Expected: verde. Si falla la concurrencia, revisar que la transacción sea `Serializable` y que el `catch` convierta `40001` en `409 APPOINTMENT_CONFLICT`.

- [ ] **Step 3: Commit**

```bash
git add DentalConnect.Tests/AppointmentProposalApiTests.cs DentalConnect/AppointmentEndpoints.cs
git commit -m "Sprint 7: cubrir concurrencia y evento de propuesta (HU-13 · DENT-13)"
```

---

### Task 7: Verificación y guardas

**Files:**
- Modify: `specs/hu-13-proponer-horario-alternativo/spec.md`
- Modify: `specs/hu-13-proponer-horario-alternativo/task.md`

**Interfaces:**
- Consumes: todo lo anterior.
- Produces: evidencia real de build, tests, migraciones y revisión.

- [ ] **Step 1: Run the full build**

Run:
```bash
dotnet build DentalConnect.slnx -c Release
```
Expected: 0 errores, 0 advertencias.

- [ ] **Step 2: Run the full test suite**

Run:
```bash
dotnet test DentalConnect.slnx -c Release
```
Expected: N/N en verde. Registrar el conteo real en `spec.md` y `task.md`; no inventarlo.
Nota: la suite API solo corre con `TEST_DATABASE_URL` (`sprint5-tests.yml`). En `deploy-aws.yml` se omite; documentarlo en la evidencia.

- [ ] **Step 3: Verify the migration cycle**

Ejecutar `database-migrations.yml` o el ciclo local con destinos explícitos (`20261110120000` → `20261109030000` → `20261110120000`). Expected: verde.

- [ ] **Step 4: Run the security and traceability gates**

- `security-reviewer` sobre el diff contra `origin/main`.
- `traceability-clerk` sobre Jira ↔ spec ↔ código ↔ test.
- Corregir los gaps y repetir.

- [ ] **Step 5: Update spec state and commit**

Cambiar `Estado` a `🚧 Borrador con evidencia` y anotar los conteos reales. No usar `✅ Aprobado` sin revisor indicado por el usuario.

```bash
git add specs/hu-13-proponer-horario-alternativo/spec.md specs/hu-13-proponer-horario-alternativo/task.md
git commit -m "Sprint 7: cerrar evidencia de propuesta de horario (HU-13 · DENT-13)"
```

---

## Execution Handoff

Plan completo. La implementación debe hacerse en una rama de la HU y con TDD. Las dependencias externas quedan así:

- Aceptar/rechazar propuesta: HU-14/DENT-14.
- Vencimiento/reversión: TEC-07/DENT-31.
- Entrega al paciente: HU-15/DENT-15.
- UI del front: PR separado en `DentalConnect-front` con DENT-13.

Riesgo principal: el snapshot EF está desincronizado; la migración es manual y el ciclo apply/revert/reapply es la única prueba real del esquema.
