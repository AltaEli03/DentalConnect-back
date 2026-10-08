# DentalConnect API

API REST del Sprint 1 para consultar clínicas odontológicas, sus servicios y especialistas.

## Requisitos

- Node.js 22+
- PostgreSQL 16+ (o Docker)

## Inicio local

1. Copia `.env.example` como `.env` y asigna un `DATABASE_URL` válido. Nunca subas ese archivo.
2. Instala dependencias con `npm ci`.
3. Genera el cliente y aplica migraciones:

   ```bash
   npm run prisma:generate
   npm run prisma:deploy
   npm run prisma:seed
   ```

4. Ejecuta `npm run dev`.

La API queda disponible en `http://localhost:3000`. Endpoints públicos:

- `GET /api/health`
- `GET /api/clinics?search=&serviceId=`
- `GET /api/clinics/:id`

Las respuestas usan `{ "data": ... }`; errores controlados usan `{ "error": { "code", "message" } }`.

## Calidad

```bash
npm run lint
npm test
npm run build
```

## Contenedores

Para desarrollo local, inicia PostgreSQL con `docker compose up -d postgres`, exporta el `DATABASE_URL` de `.env.example` y ejecuta migraciones/seed. Para iniciar la API contenida, crea un `.env` local desde el ejemplo y usa `docker compose up --build`.

## AWS y despliegue continuo

La carpeta `infra/` describe una arquitectura preparada con VPC, subredes privadas para RDS PostgreSQL, ALB público, ECS Fargate y ECR. Antes de aplicar Terraform, crea en AWS Secrets Manager un secreto cuyo valor sea la URL de PostgreSQL y pasa su ARN como `database_url_secret_arn`. No se incluyen contraseñas ni estados de Terraform en Git.

El workflow `.github/workflows/deploy-ecs.yml` se activa al hacer push a `sprint-1-Emanuel`. Se ejecuta solo cuando estén configuradas estas variables del repositorio: `AWS_DEPLOY_ROLE_ARN`, `AWS_ECR_REPOSITORY`, `AWS_ECS_CLUSTER`, `AWS_ECS_SERVICE`, `AWS_ECS_EXECUTION_ROLE_ARN`, `AWS_ECS_TASK_ROLE_ARN`, `AWS_DATABASE_URL_SECRET_ARN` y `FRONTEND_ORIGIN`. Usa OIDC, por lo que no guarda claves AWS en GitHub. El rol debe poder publicar únicamente en el ECR y actualizar únicamente el servicio ECS del proyecto.
