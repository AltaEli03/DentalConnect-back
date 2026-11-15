# Guía de revisión y aceptación de DentalConnect

Esta guía separa evidencia automatizada de comprobaciones que requieren AWS, el navegador publicado o cuentas de prueba. No marcar una prueba manual como exitosa sin ejecutarla y anotar el resultado.

## Repositorios y ramas

- Frontend: https://github.com/AltaEli03/DentalConnect-front
- Backend: https://github.com/AltaEli03/DentalConnect-back
- Integración del backend: `develop`; cambios nuevos mediante ramas `feature/*` y PR.
- Despliegue de backend: `.github/workflows/deploy-aws.yml` publica en ECS únicamente cuando se integra un push a `main`; antes ejecuta build .NET, xUnit, validación de Terraform, ejecución seed y health check HTTPS.

## Validación automatizada registrada

| Área | Evidencia | Estado comprobado |
| --- | --- | --- |
| Backend xUnit/PostgreSQL y cobertura | [Actions 43735597335](https://github.com/AltaEli03/DentalConnect-back/actions/runs/43735597335) | Éxito en `develop`, commit `4b9267b` |
| Migraciones aplicar/revertir/reaplicar | [Actions 43735597231](https://github.com/AltaEli03/DentalConnect-back/actions/runs/43735597231) | Éxito en PostgreSQL efímero; no es restauración RDS |
| Análisis estático backend | [CodeQL 43735597431](https://github.com/AltaEli03/DentalConnect-back/actions/runs/43735597431) | Ejecución exitosa; consultar Security / Code scanning para hallazgos |
| Frontend lint, 91 pruebas, 23 pares de contraste y build PWA | PR [#25](https://github.com/AltaEli03/DentalConnect-front/pull/25) | CI aprobado e integrado a `develop` |

## Verificación pública HTTPS (15-Nov-2026)

Comprobaciones GET/OPTIONS desde la sesión de trabajo, sin credenciales ni datos privados:

| Recurso | Resultado |
| --- | --- |
| [Frontend](https://main.d63zai2tg2i2u.amplifyapp.com/) y ruta `/clinics/` | HTTP 200 |
| [Manifest PWA](https://main.d63zai2tg2i2u.amplifyapp.com/manifest.webmanifest) | HTTP 200, `application/manifest+json` |
| [API health](https://d3eod9k2xa8i16.cloudfront.net/api/health) | HTTP 200 |
| `GET /api/clinics` | HTTP 200; seis clínicas de catálogo |
| `GET /api/clinics/{id}` para una clínica del catálogo | HTTP 200 |
| `OPTIONS /api/clinics` con Origin de Amplify | HTTP 204; origen y métodos CORS permitidos |
| `GET /api/clinics/{clinicId}/dentists/{dentistId}/availability?serviceId=…&date=2026-11-17` | HTTP 200; 14 espacios completos de 30 min, sin solapamientos, zona `America/Mexico_City` |
| Consulta de disponibilidad para fecha pasada (`2020-01-01`) | HTTP 400; solicitud rechazada |

La configuración de GitHub Actions también se revisó de forma no destructiva: el frontend tiene `AMPLIFY_APP_ID`, `AWS_ROLE_ARN` y `VITE_API_BASE_URL`; el backend tiene las variables requeridas de región, ECS, ECR, health, Cognito, origen del frontend y referencias a secretos. No hay valores listados como secretos de repositorio; el workflow usa OIDC y la contraseña de PostgreSQL se administra en AWS Secrets Manager según Terraform. No se leyeron valores de credenciales.

Esto confirma el acceso público al despliegue actual y sus rutas anónimas/consulta de disponibilidad. No confirma todavía el comportamiento autenticado de citas, el SHA del backend que está ejecutándose ni la restauración de respaldo. La comprobación de `/api/health` debe repetirse tras cada publicación de backend.

GitHub Actions registra el workflow de despliegue AWS exitoso en `main` para `8fab14e9683edfc94e4fb3b7fb267ae23230bc79`: [Backend CI and AWS deployment](https://github.com/AltaEli03/DentalConnect-back/actions/runs/43685117936). El backend de `develop` está en `4b9267b87262056796780d4018e2fe2239009189`; no se afirma que ese commit esté publicado. La promoción `develop` → `release` → `main` requiere completar aceptación y respaldos pendientes.

Los resultados cambian con cada commit. Consultar el SHA y la conclusión del run antes de una demostración.

## Prueba manual antes de la revisión

Completar en el sitio HTTPS actual y adjuntar capturas sin mostrar secretos ni datos personales reales:

1. Abrir el listado de consultorios y entrar a un detalle; confirmar que el API responde.
2. Iniciar como paciente verificado; revisar citas e historial propios.
3. Iniciar como usuario de consultorio; consultar solicitudes propias y probar acciones permitidas.
4. Intentar acceso cruzado con folio/ID de prueba ajeno y verificar una denegación sin datos.
5. Recorrer la interfaz con teclado, foco visible y enlace de salto; revisar etiquetas y mensajes con lector de pantalla.
6. Probar 360 px, 768 px, escritorio y zoom 200 %.
7. En AWS, verificar HTTPS del health endpoint, variables de GitHub Actions y secretos en Secrets Manager.
8. Para TEC-11, realizar recuperación a un entorno de prueba con datos ficticios. No restaurar sobre producción.

## Registro de ejecución manual

| Fecha/hora | URL y commit publicado | Casos aprobados | Defectos / responsable |
| --- | --- | --- | --- |
| 15-Nov-2026 | Frontend main Amplify; API CloudFront; API main `8fab14e` | Catálogo, filtro Tula, detalle, perfil, disponibilidad futura, rechazo de fecha pasada, CORS, manifest y SW | Sin defecto en estos casos anónimos; pendiente auth/respaldo |

## Incidencias conocidas y siguientes acciones

- La sesión de AWS estaba expirada en la inspección del 15-Nov-2026; la respuesta pública del health endpoint se comprobó, pero no se pudieron inspeccionar CloudWatch, ECS, variables/secretos ni la restauración de respaldo en consola.
- Iniciar sesión en AWS y ejecutar los puntos 7 y 8 con la configuración existente; no crear recursos hasta confirmar costo y límites de la cuenta.
- DENT-33 está en pruebas; falta llenar la matriz manual de accesibilidad y aislamiento de roles sobre el despliegue HTTPS.
