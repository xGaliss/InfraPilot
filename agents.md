# AGENTS.md

## Visión del producto

InfraPilot es una plataforma de operaciones de servidores basada en agentes.

El sistema tendrá:
- un agente instalado en cada servidor
- una API central
- una interfaz web central

El objetivo es permitir que los agentes:
- reporten su estado
- expongan capacidades disponibles
- reciban y ejecuten acciones remotas
- muestren información operativa de forma unificada

InfraPilot NO es un refactor directo de proyectos anteriores.
Es una arquitectura nueva que reutiliza lógica ya validada cuando tenga sentido.

---

## Repositorios de referencia

Los siguientes repositorios son contexto de referencia y solo deben usarse para entender lógica ya existente:

- IISentinel: lógica relacionada con IIS (sites, app pools, acciones)
- InventoryOpsMvp: lógica relacionada con servicios de Windows, tareas programadas y árbol de rutas

Reglas:
- NO copiar módulos completos de forma ciega
- NO reutilizar la estructura de proyectos antigua
- NO introducir acoplamiento fuerte con código legacy
- reutilizar ideas, patrones, providers o lógica de dominio cuando sea útil
- si una pieza legacy no encaja bien, reimplementarla limpia en la nueva arquitectura

---

## Alcance actual del MVP

El MVP es **solo para Windows**.

Capacidades iniciales:
- `services`
- `scheduledTasks`
- `fileTree`
- `iis`

No implementar todavía:
- Azure
- AWS
- Kubernetes
- Docker
- Linux
- autenticación compleja
- multi-tenant
- features enterprise no necesarias para validar el núcleo

Diseñar para crecimiento futuro, pero no sobrediseñar el MVP.

---

## Arquitectura deseada

InfraPilot debe estar basado en un **agente único** con **módulos/capabilities** activables por configuración.

### Componentes principales

#### 1. Agent
Servicio que corre en cada servidor y:
- se registra en la API central
- envía heartbeat
- publica sus capacidades
- ejecuta acciones pendientes
- reporta resultados

#### 2. Central API
Responsable de:
- registrar agentes
- guardar heartbeat y estado
- exponer inventario y detalles
- gestionar acciones pendientes
- recibir resultados de acciones

#### 3. Web UI
Responsable de:
- listar agentes
- mostrar detalle de cada agente
- mostrar pestañas o secciones dinámicas según capacidades
- permitir lanzar acciones

---

## Principios de diseño

- modularidad por capacidades
- separación clara entre:
  - transporte
  - aplicación
  - dominio
  - providers del sistema operativo
  - UI
- evitar lógica monolítica
- evitar dependencias innecesarias entre módulos
- preferir composición frente a acoplamiento
- priorizar claridad y mantenibilidad
- cada capability debe poder evolucionar de forma relativamente independiente

---

## Modelo mental correcto

Pensar InfraPilot como:

- un **core agent**
- un conjunto de **modules/providers**
- una **central API**
- una **UI basada en capacidades**

No pensar el sistema como una mezcla difusa de proyectos anteriores.

---

## Capabilities

Cada capability debe ser tratada como módulo independiente.

Ejemplos iniciales:
- `services`
- `scheduledTasks`
- `fileTree`
- `iis`

Cada módulo debería tender a tener responsabilidades como:
- obtener estado
- exponer inventario
- ejecutar acciones relacionadas con su ámbito

Evitar módulos gigantes con lógica mezclada.

---

## Convenciones recomendadas

- nombres de código, clases, proyectos, carpetas y namespaces en inglés
- comentarios breves y útiles
- evitar nombres ambiguos o heredados del legacy si ya no encajan
- preferir nombres genéricos cuando la abstracción sea real
- si una abstracción todavía no es real, no forzarla demasiado pronto

---

## Reglas sobre legacy

Los repositorios legacy son referencia, no dependencia.

Se permite:
- estudiar cómo se obtiene información de IIS
- estudiar cómo se enumeran servicios o tareas
- reaprovechar patrones o trozos concretos bien encapsulados
- rehacer código a partir de una idea ya probada

No se debe:
- copiar carpetas enteras
- mantener nombres antiguos por costumbre
- heredar deuda técnica sin revisión
- reconstruir el nuevo producto como un Frankenstein de proyectos viejos

---

## Estrategia de implementación

Antes de implementar una parte importante:
1. analizar el problema
2. proponer estructura
3. identificar qué se reutiliza y qué se reescribe
4. implementar de forma limpia
5. validar localmente

Para tareas grandes, primero proponer plan y esperar validación conceptual antes de expandir demasiado el código.

---

## Restricciones prácticas

- todo debe poder ejecutarse y probarse en local o en VM
- evitar dependencias cloud en el MVP
- evitar requerir infraestructura compleja para validar avances
- minimizar coste y fricción de pruebas

---

## Futuro esperado

A futuro, InfraPilot podría soportar:
- Linux
- más capabilities
- más tipos de web server
- más acciones operativas
- visualización más avanzada

Pero por ahora:
- diseñar con cierta previsión
- no implementar futuro innecesario
- no meter complejidad prematura

---

## Qué significa “hecho”

Una tarea está terminada solo si:
- compila
- corre localmente
- encaja con la arquitectura modular
- no rompe funcionalidades ya migradas
- no introduce acoplamiento innecesario
- es entendible y mantenible
- si cambia arquitectura o decisiones importantes, la documentación se actualiza

---

## Prioridad actual

Priorizar:
1. arquitectura limpia
2. agent core
3. central API
4. primera capability bien hecha
5. crecimiento progresivo capability por capability

No intentar construir todo a la vez.