# Versionamiento y reversión de base de datos

El esquema se versiona con **EF Core migrations**. Los archivos viven en
`DentalConnect.Infrastructure/Persistence/Migrations/` y se conservan en Git
junto con `DentalConnectDbContextModelSnapshot.cs`.

## Herramienta

EF Core migrations es gratuito, open source y ya forma parte de la solución
(`Microsoft.EntityFrameworkCore.Design` en el proyecto Web y
`Microsoft.EntityFrameworkCore.Relational` en Infrastructure). No se realizan
cambios estructurales manuales fuera de una migración. La migración de HU-13
se escribe a mano porque el snapshot de EF no incluye `Appointment`; el DDL
vive versionado en la migración.

## Aplicación

```bash
dotnet run --project DentalConnect -- --migrate
```

O con destino explícito:

```bash
dotnet ef database update 20261110120000_Sprint7AppointmentProposal \
  --project DentalConnect.Infrastructure \
  --startup-project DentalConnect
```

> No usar `dotnet ef database update` sin destino mientras el snapshot no
> incluya `Appointment`: EF Core 10 lanza `PendingModelChangesWarning` antes
> de aplicar nada.

Historial aplicado:

```sql
SELECT "MigrationId", "ProductVersion"
FROM "__EFMigrationsHistory"
ORDER BY 1;
```

## Historial de migraciones

| Migración | Fecha | Estado |
|---|---|---|
| `20261015060846_InitialCreate` | 15-Oct-2026 | Aplicada |
| `20261020120000_Sprint3DentistScheduling` | 20-Oct-2026 | Aplicada |
| `20261109000000_Sprint5Appointments` | 09-Nov-2026 | Aplicada |
| `20261109010000_Sprint5DemoDentistServices` | 09-Nov-2026 | Aplicada |
| `20261109020000_Sprint5DemoDentistSchedule` | 09-Nov-2026 | Aplicada |
| `20261109030000_Sprint6ClinicAppointmentReview` | 09-Nov-2026 | Aplicada |
| `20261110120000_Sprint7AppointmentProposal` | 10-Nov-2026 | Aplicada (local) |

## Reversión controlada

```bash
# Revertir la última migración aplicada
dotnet ef database update <MigracionAnterior> \
  --project DentalConnect.Infrastructure \
  --startup-project DentalConnect

# Revertir todo el esquema (sólo en una base desechable)
dotnet ef database update 0 \
  --project DentalConnect.Infrastructure \
  --startup-project DentalConnect
```

> La reversión a `0` elimina todas las tablas. Sólo debe ejecutarse en una base
> local desechable o en la base efímera del workflow, nunca en desarrollo
> compartido ni en producción.

## Evidencia automatizada

El workflow `.github/workflows/database-migrations.yml` levanta PostgreSQL 16 efímero
y ejecuta el ciclo con destinos explícitos:

1. Listar migraciones y obtener `LATEST` y `PREVIOUS`.
2. Aplicar `LATEST` y verificar que no queda pendiente.
3. Revertir a `PREVIOUS` y verificar que `LATEST` queda pendiente.
4. Reaplicar `LATEST` y verificar que no queda ninguna pendiente.
5. Publicar el resultado en el resumen del job.

## Evidencia a conservar

| Campo | Valor |
|---|---|
| Fecha | 10-Nov-2026 |
| Commit | `1e498dc` |
| Run de GitHub Actions | PENDIENTE (sin push/PR) |
| Migración aplicada | `20261110120000_Sprint7AppointmentProposal` |
| Resultado de la reversión | OK local (PostgreSQL 16) |
| Resultado de la reaplicación | OK local (PostgreSQL 16) |

### Salida de aplicación (local)

```text
No migrations were applied. The database is already up to date.
Done.
```

### Historial aplicado (local)

```text
20261015060846_InitialCreate
20261020120000_Sprint3DentistScheduling
20261109000000_Sprint5Appointments
20261109010000_Sprint5DemoDentistServices
20261109020000_Sprint5DemoDentistSchedule
20261109030000_Sprint6ClinicAppointmentReview
20261110120000_Sprint7AppointmentProposal
(7 rows)
```

### Salida de reversión (local)

```text
Reverting migration '20261110120000_Sprint7AppointmentProposal'.
Done.
```

### Salida de reaplicación (local)

```text
Applying migration '20261110120000_Sprint7AppointmentProposal'.
Done.
```
