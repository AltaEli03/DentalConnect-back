---
name: deferred-scope-fakes
description: Avanzar una HU de DentalConnect sin esperar infraestructura futura (entrega de notificaciones, vencimientos, email/SMS, pasarela de pago, IA) usando una interfaz con un fake reemplazable y NOTE (DENT-NN) trazables. Úsala cuando el alcance completo de una HU depende de otra spec aún no ejecutada.
---

## Propósito
Que ningún slice se bloquee por una dependencia pesada y que ningún pendiente se pierda.

## Cuándo usarla
- La HU necesita un proveedor externo que llega después (entrega de notificaciones → HU-15/TEC-07, reversión de propuesta vencida → HU-14, email/SMS, pasarela de pago).
- La infraestructura transversal aún no existe (roles de Cognito por claim → pendiente, colas de eventos fuera del outbox).

## Precondiciones
Acuerdo de alcance mínimo con el equipo y spec/issue dueño del reemplazo identificado en Jira (leído vía Atlassian MCP).

## Pasos
1. **Interfaz reemplazable** en `DentalConnect.Application` (`INotificationSender`, `IEmailSender`, `IPaymentGateway`); el slice consume la interfaz.
2. **Fake determinista** (`Fake<Nombre>`) registrado en DI para local y pruebas, sin tocar la red; en Production el fake no es seleccionable (gatear el registro por configuración).
3. Marcar cada diferido con `NOTE (ID)`, donde ID es el issue/`HU-NN` dueño (`NOTE (DENT-15)`). **Nunca `TODO`** (S1135 de CodeQL + check 1 del `security-reviewer`).
4. Registrar en las 3 capas (skill `traceability`: código + `Límites` de la spec + `task.md`).
5. **Pruebas que fijan el contrato del fake** (p. ej. "el evento outbox `APPOINTMENT_ALTERNATIVE_PROPOSED` se publica una vez por cita"), para que la spec dueña las rompa a propósito al reemplazarlo.
6. **Al llegar el issue dueño:** `grep -rn "NOTE (DENT-NN)"`, cambiar la implementación (el fake se queda para pruebas), volver a correr las suites y quitar los NOTE consumidos de las 3 capas.
7. En el **front**, el equivalente es no inventar el endpoint: la pantalla consume el contrato de la spec del back con tipos de `src/types/`, y si la respuesta aún no existe, un stub determinista tipado marcado con `NOTE (DENT-NN)`.

## Checklist
Fake detrás de interfaz · `NOTE` con dueño · 0 `TODO` · pruebas del contrato del fake · 3 capas actualizadas.

## Criterios de done
La HU cierra en "🚧 Borrador con evidencia" en su alcance y nada queda pendiente sin dueño en el tablero.

## Límites/trampas
- Solo se fakea **infraestructura** futura, nunca la lógica de negocio de la HU (las reglas de disponibilidad y estados de cita se implementan reales).
- Los fakes jamás contienen secretos, ni siquiera de prueba que parezcan reales.
- Un fake de notificaciones o de pagos no debe ser seleccionable en Production.

## Referencias
Skill `traceability`; precedent living: HU-13 difiere la reversión por vencimiento a HU-14/TEC-07 y la entrega al paciente a HU-15/TEC-07 (`specs/hu-13-proponer-horario-alternativo/spec.md`, R5 y R6).
