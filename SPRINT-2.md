# DentalConnect · Sprint 2

## Objetivo

Permitir que una persona cree una cuenta de paciente, verifique su correo e inicie y cierre sesión, manteniendo el directorio del Sprint 1.

## Historias de usuario y evidencia técnica

**HU-01 · Registro de paciente.** El formulario pide nombre, apellidos, correo, teléfono, contraseña y confirmación. Valida campos requeridos, correo, mínimo de 12 caracteres y coincidencia de contraseñas. Amazon Cognito evita usuarios duplicados y no autentica una cuenta hasta verificarla. Los registros públicos se tratan como pacientes.

**HU-02 · Verificación de correo.** Cognito envía un código de seis dígitos al registrarse. La pantalla valida seis dígitos, permite confirmar y solicitar reenvío con un temporizador de 60 segundos; el proveedor también controla los envíos. El código lo administra Cognito y no forma parte de las respuestas de la aplicación. La API Express alternativa implementa expiración a los 10 minutos, máximo de cinco intentos y reenvío con espera de 60 segundos.

**HU-03 · Inicio y cierre de sesión.** La interfaz inicia sesión mediante Cognito. Una cuenta sin confirmar recibe el error del proveedor y no se redirige a la cuenta. El acceso válido lleva a `Mi cuenta`, que muestra nombre, correo y rol `PACIENTE`. La sesión del navegador usa `sessionStorage`; cerrar sesión limpia el estado y los tokens del usuario Cognito.

**TEC-05 · Correo y códigos.** La API Express admite `EMAIL_PROVIDER=smtp` (Nodemailer con `SMTP_HOST`, `SMTP_PORT`, `SMTP_USER`, `SMTP_PASSWORD`) y `EMAIL_PROVIDER=ses` (Amazon SES v2 con `AWS_REGION` y `EMAIL_FROM`). SES usa la cadena de credenciales estándar del SDK; en AWS debe concederse `ses:SendEmail` al rol de ejecución y verificarse la identidad del remitente. En desarrollo, `EMAIL_PROVIDER=console` imprime el código solo en la consola de desarrollo, para poder probar el flujo. El modo consola está bloqueado en producción. Los códigos de la ruta Express se guardan con bcrypt, no se devuelven en la API y vencen a los diez minutos.

## Seguridad

La aplicación no guarda contraseñas ni códigos en el frontend. Cognito valida las contraseñas y confirmaciones; la interfaz mantiene tokens temporales en almacenamiento de sesión, que se elimina al cerrar sesión o la pestaña. En la API Express, contraseñas y códigos usan bcrypt con factor 12, los tokens de sesión se almacenan como SHA-256, hay límite de intentos y rate limit para autenticación, y las cookies son `HttpOnly`. La configuración de producción rechaza el secreto JWT por defecto, cookies sin `Secure`, el proveedor de correo de consola y una configuración SMTP/SES incompleta. Las claves SMTP deben guardarse en secretos del runtime; SES debe usar un rol IAM con permisos mínimos, sin claves AWS estáticas.

Los identificadores del User Pool y del cliente Cognito son configuración pública de una SPA, no claves privadas. No se guardan contraseñas SMTP, cadenas de conexión ni claves AWS en el repositorio. `.env.example` solo contiene valores de ejemplo.

## Despliegue

El flujo existente de `main` publica el frontend por AWS Amplify y el directorio de clínicas por una Lambda Python. Cognito funciona como servicio administrado de registro y verificación para la interfaz publicada. La implementación Express/Prisma/PostgreSQL —incluido este adaptador SMTP/SES— y la plantilla ECS/RDS se mantienen en el repositorio, pero no forman parte del despliegue AWS activo; encender ECS, RDS o un balanceador supondría recursos aparte. La integración SES está lista y probada en el backend fuente, pero aún no está conectada a la ruta de producción. El remitente debe verificarse en SES y el rol/runtime debe configurarse antes de activarla. No se activó SES porque es de pago por volumen y se solicitó evitar cargos.

El flujo de Sprint 2 en GitHub Actions ejecuta instalación reproducible, generación Prisma, lint, pruebas y build al actualizar `sprint-2-Emanuel`. Las publicaciones de `main` se verifican con los workflows de Amplify y Lambda.

## Pruebas

- Frontend: formulario de registro, verificación, inicio de sesión, redirección a `Mi cuenta`, mensaje para cuenta no verificada y cierre de sesión.
- Backend: rol paciente inicial, código de seis dígitos almacenado como hash, duplicados, bloqueo de acceso antes de verificar, intentos de código, perfil, sesión y revocación al salir.
- Backend email: comando SES, destinatario/remitente, plantilla de texto y HTML con escape de nombre, y código de consola visible solo en modo desarrollo.
- Build: `npm run build` en ambos repositorios.

La prueba manual de una cuenta real necesita un correo de prueba del equipo para recibir el código y confirmar el inicio de sesión en el sitio publicado; las pruebas automatizadas no envían correo ni crean usuarios reales.
