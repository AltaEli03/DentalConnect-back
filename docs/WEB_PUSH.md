# Notificaciones push

DentalConnect conserva sus avisos dentro de la aplicación. Web Push es un canal adicional que el usuario activa voluntariamente para cada navegador o dispositivo.

## Claves de aplicación

Genera una sola pareja VAPID para producción y guárdala en AWS Secrets Manager como un secreto JSON con estas claves:

- `VAPID_PUBLIC_KEY`
- `VAPID_PRIVATE_KEY`
- `VAPID_SUBJECT` (un correo de soporte con prefijo `mailto:`)

La clave privada nunca debe ir al repositorio, a variables públicas de Amplify ni a un bundle frontend. Configura el ARN del secreto en la variable de repositorio `AWS_VAPID_SECRET_ARN`. El rol de ejecución de ECS necesita `secretsmanager:GetSecretValue` limitado a ese secreto. La tarea recibe los tres valores como secretos de entorno.

La API entrega únicamente la clave pública en `GET /api/push/config`. El frontend la usa al pedir permiso y crear la suscripción.

## Persistencia y operación

El usuario debe estar autenticado y pulsar “Activar avisos” para que el navegador conceda permiso. La API asocia el endpoint y las claves de cifrado de la suscripción a la cuenta o consultorio autenticado. Al desactivar, elimina esa suscripción. Las suscripciones vencidas se limpian al responder el proveedor de push con 404 o 410.

Las notificaciones push se derivan de los mismos registros `UserNotification` que alimentan la bandeja interna. La cola y los intentos de entrega usan PostgreSQL. No se incluyen contraseñas ni información clínica en el payload.

## Publicación

1. Crear el secreto VAPID en Secrets Manager.
2. Permitir que el rol de ejecución ECS lea ese ARN.
3. Configurar `AWS_VAPID_SECRET_ARN` en las variables del repositorio.
4. Desplegar el backend para aplicar la migración EF Core y la configuración del worker.
5. Desplegar el frontend y comprobar en `/notificaciones` que aparece el botón para activar push.

Sin el secreto VAPID, la bandeja interna sigue disponible y la API responde `configured: false`; no se solicita permiso del navegador.
