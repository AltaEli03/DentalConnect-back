# Respaldo y restauración de PostgreSQL

## Respaldo de AWS

Terraform declara PostgreSQL 16 en RDS con cifrado en almacenamiento, acceso privado, protección contra eliminación y retención de respaldos de un día (`infra/main.tf`). Las credenciales maestras las gestiona AWS Secrets Manager y ECS las consume durante el despliegue; los valores no se guardan en GitHub ni en el repositorio.

La infraestructura declara las propiedades de respaldo, pero eso no confirma por sí solo que haya un snapshot activo en AWS. El 15-Nov-2026 se confirmó que la sesión de la consola estaba activa y que existe el clúster ECS `dentalconnect`; la página del servicio perdió conexión antes de mostrar sus detalles. El estado de RDS y sus snapshots sigue pendiente de verificación.

## Ensayo de respaldo/restauración con datos ficticios

El workflow `.github/workflows/database-backup-restore.yml` usa un PostgreSQL 16 efímero de GitHub Actions, sin conectarse a RDS y sin generar cargos en AWS:

1. Crea clínica, dentista y solicitud ficticios con claves foráneas.
2. Ejecuta `pg_dump` en formato custom.
3. Restaura a una base de datos vacía distinta mediante `pg_restore`.
4. Verifica conteos y que las relaciones sobreviven a la restauración.
5. Elimina el archivo de dump temporal al terminar.

Ejecutar localmente sobre una instancia desechable también es posible con `PGHOST`, `PGPORT`, `PGUSER` y `PGPASSWORD` configurados:

```bash
bash scripts/verify-backup-restore.sh
```

El test es reproducible y demuestra el formato/destino de restauración. No sustituye la restauración de un snapshot nativo de RDS. Para cerrar TEC-11 debe revisarse en AWS el snapshot, política de retención y restauración aislada si la política/costo de la cuenta lo permiten.

## Evidencia

[Ejecución exitosa de PostgreSQL fictitious-data backup and restore (develop)](https://github.com/AltaEli03/DentalConnect-back/actions/runs/43743386444). El ensayo automatizado no demuestra aún la restauración de un snapshot real de RDS.
