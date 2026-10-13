# DentalConnect · Sprint 2

## Objetivo

Permitir que una persona cree una cuenta de paciente, verifique su correo e inicie y cierre sesión, manteniendo el directorio del Sprint 1.

## Historias de usuario y evidencia técnica

**HU-01 · Registro de paciente.** El formulario pide nombre, apellidos, correo, teléfono, contraseña y confirmación. Valida campos requeridos, correo, mínimo de 12 caracteres y coincidencia de contraseñas. El endpoint productivo `/api/auth/register` crea la identidad como paciente en Amazon Cognito, suprime el correo predeterminado de Cognito y rechaza correos duplicados. La contraseña se entrega por HTTPS a Cognito y no se persiste en DynamoDB ni se devuelve al navegador.

**HU-02 · Verificación de correo.** La Lambda productiva genera un código de seis dígitos y lo envía con Brevo SMTP. Su hash HMAC con secreto privado se guarda en la tabla existente `DentalConnectClinics`, junto con vencimiento a los 10 minutos y contador de intentos; nunca se guarda ni se devuelve el código plano. Hay máximo de cinco intentos y cinco reenvíos, con una espera mínima de 60 segundos. Un código válido marca el correo verificado en Cognito y activa la contraseña para iniciar sesión. La consulta pública de clínicas excluye los registros de verificación.

**HU-03 · Inicio y cierre de sesión.** La interfaz inicia sesión mediante Cognito. Una cuenta sin confirmar recibe el error del proveedor y no se redirige a la cuenta. El acceso válido lleva a `Mi cuenta`, que muestra nombre, correo y rol `PACIENTE`. La sesión del navegador usa `sessionStorage`; cerrar sesión limpia el estado y los tokens del usuario Cognito.

**TEC-05 · Correo y códigos.** El flujo web productivo usa Brevo SMTP (plan gratuito sujeto a los límites y políticas vigentes de Brevo) desde Lambda con TLS. Se requieren `SMTP_HOST=smtp-relay.brevo.com`, `SMTP_PORT=587`, `SMTP_USER`, `SMTP_PASSWORD`, `EMAIL_FROM`, `AUTH_CODE_PEPPER` (secreto aleatorio de al menos 32 caracteres), `COGNITO_USER_POOL_ID` y `FRONTEND_ORIGIN`. Las credenciales y el pepper van en variables cifradas de Lambda, nunca en GitHub ni en el código. La API Express fuente también admite SMTP/SES y modo consola solo para desarrollo; ese servidor Express no es el runtime publicado.

## Seguridad

La aplicación no almacena contraseñas después de procesar la solicitud ni guarda códigos en el frontend. El código se compara con `hmac.compare_digest`, está ligado al correo y se protege con `AUTH_CODE_PEPPER`; los errores y logs nunca imprimen credenciales o códigos. Cognito administra autenticación e identidad, y los tokens del navegador permanecen en `sessionStorage`. La política `DentalConnectAuthLimitedAccess`, limitada a este User Pool y a la tabla existente, ya está asociada al rol `DentalConnectLambdaExecution`. Las variables privadas se cifran en Lambda; no se usan claves AWS estáticas.

Los identificadores del User Pool y del cliente Cognito son configuración pública de una SPA, no claves privadas. No se guardan contraseñas SMTP, cadenas de conexión ni claves AWS en el repositorio. `.env.example` solo contiene valores de ejemplo.

## Despliegue

El flujo de `main` publica el frontend por AWS Amplify y el directorio/API de autenticación por la Lambda Python existente. El cambio de código se publica con el workflow de GitHub Actions en la Lambda actual; Amplify publica el frontend. No se requiere encender ECS, RDS o balanceador. El plan gratuito de Brevo permite probar el correo dentro de sus límites publicados, pero AWS puede cobrar si se exceden las cuotas gratuitas; revisar la cuenta y las cuotas antes de usarlo en producción.

El flujo de Sprint 2 en GitHub Actions ejecuta instalación reproducible, generación Prisma, lint, pruebas y build al actualizar `sprint-2-Emanuel`. Las publicaciones de `main` se verifican con los workflows de Amplify y Lambda.

## Pruebas

- Frontend: formulario de registro, verificación, inicio de sesión, redirección a `Mi cuenta`, mensaje para cuenta no verificada y cierre de sesión.
- Backend: rol paciente inicial, código de seis dígitos almacenado como hash, duplicados, bloqueo de acceso antes de verificar, intentos de código, perfil, sesión y revocación al salir.
- Backend email: comando SES, destinatario/remitente, plantilla de texto y HTML con escape de nombre, y código de consola visible solo en modo desarrollo.
- Build: `npm run build` en ambos repositorios.

El rol ya cuenta con los permisos mínimos de DynamoDB y Cognito. En Lambda están configurados `COGNITO_USER_POOL_ID`, `SMTP_HOST`, `SMTP_PORT` y `FRONTEND_ORIGIN`. Falta crear/verificar la cuenta y el remitente de Brevo y agregar directamente en Lambda `SMTP_USER`, `SMTP_PASSWORD`, `EMAIL_FROM` y `AUTH_CODE_PEPPER`; esas credenciales no deben compartirse por chat ni guardarse en Git. Las pruebas automatizadas no envían correo ni crean usuarios reales. Hasta configurar esas cuatro variables, desplegar `main` y verificar en producción el envío, verificación e inicio/cierre de sesión, el Sprint 2 no debe declararse completamente validado.
