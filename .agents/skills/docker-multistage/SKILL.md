---
name: docker-multistage
description: Construir la imagen de la API de DentalConnect de forma reproducible y segura (multi-stage SDK → aspnet, versiones fijadas, usuario no root, sin secretos). Úsala al modificar el Dockerfile, el .dockerignore o el docker-compose local del back.
---

## Propósito
Una imagen pequeña, reproducible y sin secretos para el despliegue en AWS (ECS: `infra/ecs-task-definition.json`, workflow `deploy-aws.yml`).

## Cuándo usarla
Al tocar `Dockerfile`, `.dockerignore` o `docker-compose.yml` de `DentalConnect-back`, o al actualizar el SDK o el runtime. El front no usa imagen: build estático desplegado en S3/CloudFront (`deploy-s3-cloudfront.yml`).

## Precondiciones
Versiones verificadas en MCR (`mcr.microsoft.com/v2/dotnet/{sdk,aspnet}/tags/list`) y en Docker Hub (postgres). Hoy el Dockerfile fija `mcr.microsoft.com/dotnet/sdk:10.0` y `mcr.microsoft.com/dotnet/aspnet:10.0`; no subir de tag sin medir.

## Pasos
1. **Stage `build`:** `mcr.microsoft.com/dotnet/sdk:10.0`; copiar primero los cuatro `.csproj` (Domain → Application → Infrastructure → DentalConnect) → `dotnet restore` (capa cacheable) → copiar el código → `dotnet build`/`publish -c Release -o /app/publish /p:UseAppHost=false`.
2. **Stage `base/final`:** `mcr.microsoft.com/dotnet/aspnet:10.0`, `USER $APP_UID` (usuario no root de las imágenes oficiales), `ENV ASPNETCORE_URLS=http://+:3000`, `EXPOSE 3000`. El trust bundle de RDS se añade como `root` y se vuelve a `$APP_UID` inmediatamente (ver Dockerfile): no dejar el stage final corriendo como root.
3. **`.dockerignore`** deja fuera `.git`, `.vs`, `bin/`, `obj/`, `specs/`, `docs/`, pruebas, `appsettings*.Local.json`, `.env` y las llaves.
4. **Secretos:** nunca con `COPY`, `ARG` ni `ENV`; llegan en runtime por variables o secretos del orquestador (ECS task definition / SSM). Connection strings sin contraseña en archivos versionados (solo `.env.example`).
5. **Verificar:** job de `deploy-aws.yml` que corre `docker build --tag dentalconnect-api:ci .` (sin push en PRs).
6. **Compose local:** `postgres:16-alpine` con versión fija, puerto solo en `127.0.0.1`, contraseña desde env ignorado con `${POSTGRES_PASSWORD:?…}`, volumen para `/var/lib/postgresql/data` y healthcheck (`pg_isready`).

## Checklist
Versiones fijadas · usuario no root en `final` · sin secretos en capas · `.dockerignore` defensivo · Dependabot (`docker`) activo si el repo lo tiene.

## Criterios de done
Build de imagen verde en `deploy-aws.yml`; `docker history` sin secretos; la imagen arranca local con `docker compose up` y la API responde (los endpoints de health/`MapGroup` según el pipeline real).

## Límites/trampas
- La imagen `aspnet` no trae `curl` ni `wget`: el health check del contenedor va en el orquestador (ECS health check) o con una imagen que los incluya.
- El escaneo de la imagen (Trivy) es un guardraíl futuro: si se agrega, medir antes de fijar umbrales (skill `empirical-verification`).
- `docker build` local puede fallar en máquinas sin Docker: el gate real es el workflow.

## Referencias
`DentalConnect-back/Dockerfile`, `.dockerignore`, `docker-compose.yml`, `infra/`, `.github/workflows/deploy-aws.yml`.
