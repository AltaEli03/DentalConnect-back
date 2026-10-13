# DentalConnect API

API y servicios de autenticación del proyecto DentalConnect. La aplicación web publicada conserva el directorio de clínicas del Sprint 1 y agrega el flujo de cuentas del Sprint 2.

## Arquitectura actual en AWS

- **Frontend:** React 19, TypeScript y Vite, publicado por AWS Amplify desde `main`.
- **Directorio:** AWS Lambda y DynamoDB. El workflow de `main` publica `lambda_function.py` y comprueba `/api/health`.
- **Cuentas en producción:** Amazon Cognito User Pools administra registro, correo de verificación e inicio/cierre de sesión. La interfaz llama a Cognito directamente y presenta las cuentas de registro como pacientes.
- **API Express:** el repositorio conserva una implementación TypeScript con Express, Prisma y PostgreSQL para el dominio y los flujos de autenticación propios. La infraestructura ECS/RDS es una plantilla; el workflow de producción actual no la aplica. No se debe afirmar que esos endpoints Node estén activos en el sitio hasta desplegar esa infraestructura.

La separación mantiene activo el Sprint 1 y deja el Sprint 2 disponible en el sitio usando Cognito, sin levantar ECS, un balanceador o RDS que agreguen cargos. El detalle de historias y pruebas está en [SPRINT-2.md](SPRINT-2.md).

## API publicada

- `GET /api/health`
- `GET /api/clinics?search=`
- `GET /api/clinics/:id`

Las respuestas usan `{ "data": ... }`; los errores controlados usan `{ "error": { "code", "message" } }`.

## Ejecución para desarrollo y pruebas

Requisitos: Node.js 22+, PostgreSQL 16+ (o Docker).

1. Copia `.env.example` a `.env` y asigna un `DATABASE_URL` de desarrollo. Nunca subas `.env`.
2. Ejecuta `npm ci`.
3. Genera el cliente y aplica las migraciones: `npm run prisma:generate`, `npm run prisma:deploy`.
4. Ejecuta `npm run dev`.

La API Express queda en `http://localhost:3000`. Para probar el registro en modo consola, revisa la terminal de desarrollo: el código se imprime únicamente cuando `NODE_ENV` no es `production`. Para correo real configura `EMAIL_PROVIDER=smtp`, `EMAIL_FROM`, `SMTP_HOST`, `SMTP_PORT`, `SMTP_USER` y `SMTP_PASSWORD` con variables de entorno. Para Amazon SES se pueden usar sus credenciales SMTP; no se guardan claves AWS en el código.

Controles de producción del servidor Express: exige `JWT_SECRET` propio de 32 caracteres o más, cookies `Secure`, proveedor SMTP configurado y `COOKIE_SECURE=true`; el modo consola no inicia en producción. Las contraseñas y códigos de verificación se almacenan con bcrypt; nunca se devuelven códigos en las respuestas.

## Verificación de cambios

```bash
npm ci
npm run prisma:generate
npm run lint
npm test
npm run build
```

GitHub Actions ejecuta CI en `sprint-2-Emanuel`. En `main`, el frontend se publica en Amplify y el workflow backend actualiza la Lambda del directorio usando OIDC. Los flujos requieren las variables de repositorio descritas en los archivos de workflow; no se deben guardar claves AWS de larga duración.
