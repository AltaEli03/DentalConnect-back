# Versionamiento y reversión de base de datos

El esquema se versiona con **EF Core migrations**. Los archivos viven en
`DentalConnect.Infrastructure/Persistence/Migrations/` y se conservan en Git
junto con `DentalConnectDbContextModelSnapshot.cs`.

## Herramienta

EF Core migrations es gratuito, open source y ya forma parte de la solución
(`Microsoft.EntityFrameworkCore.Design` en el proyecto Web y
`Microsoft.EntityFrameworkCore.Relational` en Infrastructure). No se realizan
cambios estructurales manuales en el administrador de base de datos.

## Aplicación

```bash
dotnet ef database update \
  --project DentalConnect.Infrastructure \
  --startup-project DentalConnect
```

Historial aplicado:

```sql
SELECT "MigrationId", "ProductVersion"
FROM "__EFMigrationsHistory"
ORDER BY 1;
```

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

El workflow `.github/workflows/db-migrations.yml` levanta PostgreSQL 16 efímero
y ejecuta el ciclo completo:

1. Aplicar todas las migraciones.
2. Mostrar `__EFMigrationsHistory`.
3. Revertir a `0`.
4. Verificar que no quedan tablas (`\dt`).
5. Reaplicar todas las migraciones.

## Evidencia a conservar

| Campo | Valor |
|---|---|
| Fecha | PENDIENTE |
| Commit | PENDIENTE |
| Run de GitHub Actions | PENDIENTE |
| Migración aplicada | `20261015060846_InitialCreate` |
| Resultado de la reversión | PENDIENTE |
| Resultado de la reaplicación | PENDIENTE |

### Salida de aplicación (pegar salida real)

```text
PENDIENTE
```

### Historial aplicado (pegar salida real)

```text
PENDIENTE
```

### Salida de reversión (pegar salida real)

```text
PENDIENTE
```
