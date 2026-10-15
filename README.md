# DentalConnect API

API REST de DentalConnect desarrollada con **.NET 10** y **ASP.NET Core Web API**, organizada con arquitectura limpia (Clean Architecture) mediante proyectos de biblioteca de clases independientes. Persiste datos en **PostgreSQL** con Entity Framework Core y autentica a los pacientes con **Amazon Cognito**.

Es el backend del frontend [DentalConnect-front](https://github.com/AltaEli03/DentalConnect-front).

## Tabla de contenido

- [Arquitectura](#arquitectura)
- [Estructura del repositorio](#estructura-del-repositorio)
- [Requisitos](#requisitos)
- [Configuración](#configuración)
- [Ejecución local](#ejecución-local)
- [Base de datos](#base-de-datos)
- [Endpoints](#endpoints)
- [Pruebas](#pruebas)
- [Despliegue en AWS](#despliegue-en-aws)
- [Integración con el frontend](#integración-con-el-frontend)

## Arquitectura

La solución se divide en cuatro capas, cada una en su propio proyecto. Las dependencias sólo apuntan hacia adentro:

```text
DentalConnect (Web API)  ──►  DentalConnect.Infrastructure  ──►  DentalConnect.Application  ──►  DentalConnect.Domain
```

| Proyecto | Responsabilidad | Dependencias |
|---|---|---|
| `DentalConnect.Domain` | Entidades del catálogo (`Clinic`, `DentalService`, `ClinicService`, `Dentist`) y generador de IDs. | Ninguna |
| `DentalConnect.Application` | DTOs, validación de entrada, interfaces (`IClinicRepository`, `IIdentityProvider`) y `AuthService`. | Domain |
| `DentalConnect.Infrastructure` | EF Core (`DentalConnectDbContext`, migraciones, seeder), adaptador de Cognito (`CognitoIdentityProvider`). | Application, Domain, AWS SDK |
| `DentalConnect` | Host ASP.NET Core: endpoints, CORS, limitación de peticiones, inyección de dependencias. | Application, Infrastructure |
| `DentalConnect.Tests` | Pruebas de integración de la API con repositorios e identidad simulados. | Todos |

Sólo `DentalConnect.Infrastructure` referencia el SDK de AWS.

## Estructura del repositorio

```text
.
├── DentalConnect.slnx
├── DentalConnect/                 # Host Web API y Program.cs
├── DentalConnect.Application/     # Casos de uso, DTOs, validación
├── DentalConnect.Domain/          # Entidades
├── DentalConnect.Infrastructure/  # EF Core, migraciones, Cognito
├── DentalConnect.Tests/           # Pruebas (xUnit)
├── infra/                         # Terraform: VPC, ECS Fargate, RDS, ECR, ALB
├── Dockerfile                     # Imagen multi-proyecto (puerto 3000)
├── docker-compose.yml             # PostgreSQL 16 + API para desarrollo local
└── .env.example                   # Variables de entorno de referencia
```

## Requisitos

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- [Docker](https://www.docker.com/) (opcional, para PostgreSQL y la imagen de la API)
- PostgreSQL 16 (o usar el de `docker-compose.yml`)
- Herramienta `dotnet-ef` para generar migraciones:

  ```bash
  dotnet tool install --global dotnet-ef --version 10.0.*
  ```

## Configuración

Copie `.env.example` como referencia y defina las variables en su entorno. Variables principales:

| Variable | Obligatoria | Descripción |
|---|---|---|
| `DATABASE_URL` | Sí\* | Cadena de conexión en formato URI, por ejemplo `postgresql://usuario:clave@host:5432/dentalconnect?schema=public`. |
| `ConnectionStrings__DefaultConnection` | Sí\* | Alternativa en formato Npgsql (`Host=...;Database=...`). |
| `COGNITO_USER_POOL_ID` | Sí\*\* | Identificador del user pool de Amazon Cognito (`us-east-1_...`). |
| `COGNITO_CLIENT_ID` | Sí\*\* | Identificador del app client de Cognito, sin secreto. |
| `AWS_REGION` | No | Región de AWS. Valor por defecto: `us-east-1`. |
| `FRONTEND_ORIGIN` | No | Orígenes permitidos por CORS, separados por comas. Valor por defecto: `http://localhost:5173`. |
| `PORT` | No | Puerto de escucha. Valor por defecto: `3000` (si no se define `ASPNETCORE_URLS`). |
| `ASPNETCORE_ENVIRONMENT` | No | `Development`, `Production`, etc. |

\* Si no se define, se usa `Host=localhost;Port=5432;Database=dentalconnect;...`.
\*\* Obligatorias fuera de `Development`; si faltan, la aplicación no arranca.

La API no emite tokens ni cookies de sesión. El inicio de sesión lo realiza el frontend directamente contra Cognito.

## Ejecución local

### Con Docker Compose (recomendado)

Levanta PostgreSQL 16 y la API en el puerto `3000`:

```bash
docker compose up --build
```

Después, cargue el catálogo de ejemplo (ver [Base de datos](#base-de-datos)).

### Con .NET directamente

1. Inicie PostgreSQL (por ejemplo, `docker compose up -d postgres`).
2. Defina las variables de entorno necesarias, o use `ASPNETCORE_ENVIRONMENT=Development` para omitir las de Cognito.
3. Ejecute la API:

   ```bash
   dotnet run --project DentalConnect
   ```

En `Development` se expone la documentación OpenAPI en `/openapi/v1.json`.

## Base de datos

El esquema se crea con migraciones de EF Core. La migración inicial es `InitialCreate`.

Para aplicar las migraciones y cargar el catálogo de ejemplo:

```bash
dotnet run --project DentalConnect -- --seed
```

El modo `--seed` ejecuta las migraciones pendientes y después carga el catálogo: 6 servicios dentales, 3 consultorios (Puebla, Ciudad de México y Monterrey), 9 ofertas consultorio-servicio con precio y 4 dentistas. La API no aplica migraciones al iniciar.

Para crear una nueva migración:

```bash
dotnet ef migrations add NombreMigracion --project DentalConnect.Infrastructure --startup-project DentalConnect --output-dir Persistence/Migrations
```

### Esquema

| Tabla | Clave primaria | Relaciones |
|---|---|---|
| `Clinic` | `id` | — |
| `DentalService` | `id` | — |
| `ClinicService` | `id` | FK a `Clinic` (`CASCADE`) y a `DentalService` (`RESTRICT`). Único en `(clinicId, dentalServiceId)`. `priceFrom` es `numeric(10,2)`. |
| `Dentist` | `id` | FK a `Clinic` (`CASCADE`). |

Los datos de usuarios viven en Amazon Cognito, no en PostgreSQL.

## Endpoints

Todas las rutas empiezan con `/api`. Las respuestas exitosas usan el sobre `{ "data": ... }` y los errores el sobre `{ "error": { "code", "message" } }`.

### Salud

| Método | Ruta | Respuesta |
|---|---|---|
| `GET` | `/api/health` | `200` `{"status":"ok","service":"DentalConnect API"}` |

### Consultorios

| Método | Ruta | Descripción |
|---|---|---|
| `GET` | `/api/clinics?search=&serviceId=` | Lista consultorios activos ordenados por nombre. `search` busca en nombre, municipio y estado (sin distinguir mayúsculas). `serviceId` filtra por servicio activo. |
| `GET` | `/api/clinics/{id}` | Detalle de un consultorio con servicios y dentistas activos. `404` `CLINIC_NOT_FOUND` si no existe o está inactivo. |

### Autenticación y verificación (Amazon Cognito)

Estas rutas están limitadas a **20 peticiones por IP cada 15 minutos** (respuesta `429` `RATE_LIMITED`).

| Método | Ruta | Descripción | Respuesta exitosa |
|---|---|---|---|
| `POST` | `/api/auth/register` | Registra un paciente en Cognito. Body: `firstName`, `lastName`, `email`, `phone`, `password` (12–128 caracteres) y `confirmPassword` opcional. El teléfono se normaliza a E.164 (`+52` por defecto). | `201` |
| `POST` | `/api/auth/verify-email` | Confirma el registro con `email` y un `code` de 6 dígitos. | `200` |
| `POST` | `/api/auth/resend-verification-code` | Solicita un nuevo código. Responde igual para correos existentes o no. | `202` |

Cualquier otra ruta, incluida `POST /api/auth/login`, responde `404` `NOT_FOUND`.

### Códigos de error

| Código HTTP | `error.code` | Caso |
|---|---|---|
| `400` | `VALIDATION_ERROR` | Datos inválidos (incluye `details.fieldErrors`). |
| `400` | `INVALID_VERIFICATION_CODE` | Código incorrecto, expirado o cuenta inexistente. |
| `404` | `CLINIC_NOT_FOUND` | Consultorio inexistente o inactivo. |
| `404` | `NOT_FOUND` | Ruta inexistente. |
| `409` | `EMAIL_EXISTS` | Correo ya registrado. |
| `429` | `RATE_LIMITED` | Demasiadas peticiones. |
| `500` | `INTERNAL_ERROR` | Error no controlado. |

### Ejemplo

```bash
curl "http://localhost:3000/api/clinics?search=Puebla"

curl -X POST http://localhost:3000/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"firstName":"Ana","lastName":"García","email":"ana@ejemplo.com","phone":"5512345678","password":"SecurePassword123!"}'
```

## Pruebas

```bash
dotnet test
```

Las pruebas (`DentalConnect.Tests/ApiTests.cs`) levantan la API en memoria con un repositorio y un proveedor de identidad simulados, así que no requieren PostgreSQL ni llamadas a AWS.

## Despliegue en AWS

La infraestructura está definida con Terraform en `infra/`:

- VPC con subredes públicas y privadas.
- ECS Fargate para la API, detrás de un Application Load Balancer (health check en `/api/health`).
- RDS PostgreSQL 16 con la contraseña administrada por Secrets Manager.
- Repositorio ECR para la imagen.
- Grupo de logs de CloudWatch.

Pasos generales:

```bash
cp infra/terraform.tfvars.example infra/terraform.tfvars   # complete los valores
terraform -chdir=infra init
terraform -chdir=infra plan
terraform -chdir=infra apply
```

El archivo `terraform.tfvars` y el estado de Terraform no deben subirse al repositorio.

Variables de Terraform relevantes: `frontend_origin`, `cognito_user_pool_id`, `cognito_client_id`, `database_url_secret_arn` y `container_image`.

La imagen se construye con el `Dockerfile` de la raíz, que compila todos los proyectos y expone el puerto `3000`.

### Consideraciones para producción

- El app client de Cognito debe **no** tener secreto, porque el frontend inicia sesión sin él.
- Cognito envía los correos con su remitente por defecto, que tiene una cuota diaria baja. Configure Amazon SES antes de producción.
- Si el user pool tiene activado "prevent user existence errors", un registro duplicado puede responder con éxito en lugar de `EMAIL_EXISTS`.

## Integración con el frontend

El frontend ([DentalConnect-front](https://github.com/AltaEli03/DentalConnect-front)) usa la variable `VITE_API_BASE_URL` para apuntar a esta API, por ejemplo `http://localhost:3000/api`. Añada la URL del frontend a `FRONTEND_ORIGIN` para que CORS la permita.