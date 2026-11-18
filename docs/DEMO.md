# Guion de la demo — "Your AI Has Amnesia. Let's Fix That."

Los comandos exactos, en orden. Ensaya en una rama desechable (ver "Reiniciar entre ensayos").

> **Regla de idioma.** El texto del grafo (specs, docs, la columna de capacidades) está en
> español, así que las preguntas al grafo y a Claude van en español. El código, los comandos y
> los identificadores se quedan en inglés.

**Qué está verificado y qué no** (al 2026-11-17):

- Verificado: la app de punta a punta (cada endpoint a través del gateway, el flujo de eventos,
  las tres páginas); los pasos 1 y 3 tal como están escritos; y el lado del grafo de los pasos
  4–6 con un cambio de prueba: UNBOUND antes de registrar en la columna de capacidades, la spec
  `IN_FLIGHT` y el cambio activo visibles sin commit, `openspec archive`, el aviso STALE,
  `ingest resync`, el cambio de `IN_FLIGHT` → `CURRENT`, y el servidor MCP `kg`.
- **Todavía no se ha corrido:** las tres skills `/kindred-*` en sí, manejadas por un agente en
  una sesión nueva, construyendo la feature real (los pasos 4–6 completos): eso es el ensayo.
  Tampoco `scripts/start-dev.ps1` en su camino feliz: durante la preparación otro stack tenía
  ocupados los puertos por defecto, así que solo se probó su revisión previa de puertos y los
  servicios se levantaron a mano.
- **Todavía no se ha vuelto a verificar contra un grafo real en español:** las preguntas al
  grafo y a Claude traducidas al español (pasos 2, 3, 4 y 6). La verificación de arriba se hizo
  con el texto y las preguntas en inglés; los resultados esperados que se describen abajo pueden
  cambiar ahora que el texto del grafo está en español. Lo único medido en español es la
  búsqueda por similitud, fuera de Neo4j y con el mismo modelo: para las dos preguntas del
  paso 3, la capacidad correcta queda en primer lugar (`study-group-matching` con 0.89,
  `course-enrollment` con 0.86), pero las specs quedan más abajo que en inglés
  (`course-rosters` en el lugar 10 de 17), así que llegan por recorrido del grafo y no como
  resultado directo. El orden de lo que imprime `q query` puede ser distinto al de antes.

## 0. Antes de la charla (una vez, fuera del escenario)

```powershell
# Kindred necesita los puertos 5432, 5672, 15672, 7474, 7687, 5000, 5101, 5102, 4200.
# Detén lo que los tenga ocupados (el script de arranque se niega a correr y dice quién es).

cd C:\Projects\KINDRED
dotnet build Kindred.slnx                 # también compila el exe que corre el servidor MCP kg
npm install --prefix apps/frontend        # solo si falta node_modules

# Desde cero: bases de datos vacías, grafo vacío.
./scripts/stop-dev.ps1 -Wipe
./scripts/start-dev.ps1 -Kg               # postgres, rabbitmq, neo4j, servicios, frontend

# Un nombre corto para la herramienta del grafo de conocimiento, en esta terminal:
Set-Alias kg (Resolve-Path tools/Kindred.KnowledgeGraph/bin/Debug/net10.0/Kindred.KnowledgeGraph.exe)
```

Abre Claude Code en `C:\Projects\KINDRED` **después** del build, para que arranque el servidor
MCP `kg` (`/mcp` debe listar `kg` con `kg_query` y `kg_stats`). No recompiles
`tools/Kindred.KnowledgeGraph` mientras haya una sesión abierta: el servidor en ejecución
bloquea el exe.

Revisa: http://localhost:4200 muestra doce estudiantes; `git status` está limpio; `git log`
muestra el commit de v1.

## 1. Recorrido por la app (2 min)

http://localhost:4200 → entra como **Valeria Hernández** → **Mis cursos** (inscríbete en uno y
dalo de baja otra vez; Valeria empieza con 4 cursos, así que inscríbete en dos más para llegar a
seis y luego intenta un séptimo para mostrar la regla) → **Compañeros** (agrupados
por curso, con insignias de cursos en común).

Opcional, para mostrar que los dos servicios de verdad están separados:
http://localhost:5000/api/v1/matching/courses/COMP-248/roster — la copia propia de
matching-service, construida solo con eventos. UI de RabbitMQ: http://localhost:15672
(`kindred` / `kindred_dev`).

## 2. "La IA tiene amnesia"

Una sesión nueva de Claude Code en una carpeta vacía (o `claude` sin proyecto): pregunta
*"¿Kindred tiene formación de grupos de estudio? ¿Qué se rompería si cambio la inscripción?"*
No tiene forma de saberlo.

## 3. Dale memoria — construye el grafo

```powershell
kg ingest run        # hechos deterministas + convenciones, sin LLM  → 0 UNBOUND, 0 rejected
kg ingest embed      # embeddings locales, sin API key               → cobertura completa por label
kg q stats           # qué hay adentro
```

Navegador de Neo4j, para la imagen: http://localhost:7474 (`neo4j` / `kindred_kg_dev`)

```cypher
MATCH (n:KgNode)-[r]->(m:KgNode) RETURN n, r, m
```

Ahora pregúntale cosas:

```powershell
kg q query "¿existe la formación de grupos de estudio?" --kind existence
```

→ `capability:study-group-matching` (declarada en la columna de capacidades: "Hoy, las listas
de cada curso; después, formar grupos de estudio …") y `spec:course-rosters`. No hay spec,
feature ni cambio de formación de grupos. **Respuesta con citas: todavía no.**

```powershell
kg q query "¿qué toca la inscripción a cursos?" --kind blast
```

→ la línea para leer en voz alta:

```
service:student-service -[PUBLISHES]-> event:StudentEnrolledInCourseIntegrationEvent   src=…/Commands/EnrollInCourse.cs@<commit>
event:StudentEnrolledInCourseIntegrationEvent -[CONSUMED_BY]-> service:matching-service src=…/Application/EnrollmentEventHandlers.cs@<commit>
module:Matching -[BRIDGES]-> module:Students via=student-enrolled-in-course
service:gateway -[CALLS]-> service:student-service routes=[/api/v1/students/{**rest}, /api/v1/courses/{**rest}]
```

Cada hecho dice de qué archivo y de qué commit salió. Nadie escribió estas aristas a mano: se
leen del código.

## 4. Propón la feature

En Claude Code:

```
/kindred-propose add-availability-and-group-matching

Los estudiantes declaran su disponibilidad semanal (días de la semana × mañana / tarde / noche).
matching-service forma grupos de estudio: estudiantes que toman el mismo curso y coinciden en al
menos un bloque de disponibilidad, en grupos de hasta cuatro. El frontend recibe un editor de
disponibilidad en "Mis cursos" y una página nueva, "Mis grupos". Mantenlo tan pequeño como el
resto de Kindred.
```

Lo que ve el público, en orden:

1. **Compuerta de existencia** — el agente consulta el grafo *antes de crear nada* y reporta,
   con citas, que la formación de grupos de estudio todavía no existe.
2. **Cambio de OpenSpec** — `openspec/changes/add-availability-and-group-matching/` con
   propuesta, diseño, deltas de spec y tareas (una sección por servicio + frontend).
3. **Registro en la columna de capacidades** — el nombre del cambio se agrega a
   `study-group-matching` en `knowledge-graph/capabilities.yaml`, y luego `ingest run`.
4. **El grafo ya lo sabe** — pregunta otra vez:

```powershell
kg q query "¿existe la formación de grupos de estudio?" --kind existence
```

→ `change:add-availability-and-group-matching … change=active tasks=0/N` y una spec con
`authority=IN_FLIGHT`. El trabajo en curso está en la memoria desde el primer minuto: un
segundo agente que hiciera la misma pregunta ahora no empezaría un duplicado.

## 5. Constrúyela con equipos en paralelo

```
/kindred-apply add-availability-and-group-matching --plan-teams-only
```

Muestra la división **sin gastar nada**: el radio de impacto según el grafo, el plan de olas y
la matriz de propiedad de archivos (un equipo por servicio + uno para el frontend; el kernel
compartido y el gateway se los queda el orquestador). Luego, en serio:

```
/kindred-apply add-availability-and-group-matching
```

La ola 0 escribe el contrato del evento de integración; luego tres agentes trabajan al mismo
tiempo; luego la compuerta de la ola: `dotnet build`, `dotnet test`, el build del frontend y las
migraciones.

**Antes de que corra la compuerta de la ola, cierra las tres ventanas de los servicios** (deja
Docker corriendo): un servicio en ejecución bloquea sus dlls y el build falla con "archivo en
uso". Después vuelve a levantarlos:

```powershell
./scripts/start-dev.ps1 -Kg      # Docker ya está arriba, así que esto solo recompila y vuelve a abrir los servicios
```

Luego muestra la feature en el navegador: define tu disponibilidad, abre **Mis grupos**.

> Este paso tarda varios minutos. Habla mientras tanto (la matriz, la regla de "un solo
> escritor por archivo", por qué los agentes nunca hacen commit), o salta a una rama de ensayo
> donde ya esté construida.

## 6. Publícala — el grafo se acuerda

```
/kindred-commit-n-archive add-availability-and-group-matching
```

Archivar (los deltas de spec se vuelven specs principales) → **tú apruebas el commit** →
`ingest resync` + `ingest embed`. Luego la consulta de cierre, la misma pregunta del paso 3 con
otra respuesta:

```powershell
kg q query "¿existe la formación de grupos de estudio?" --kind existence
```

→ la spec es `authority=CURRENT`, el cambio es `change=archived`, y el evento nuevo aparece en
el radio de impacto de la inscripción. Abre una sesión de Claude Code completamente nueva y
hazle la pregunta del paso 2. Esta vez sí lo sabe.

## Reiniciar entre ensayos

Ensaya en una rama para que `main` se quede en v1:

```powershell
git switch -c rehearsal            # antes del paso 4
# … corre los pasos 3–6 …
git switch main
git branch -D rehearsal
git clean -fd                      # borra todo lo que el ensayo dejó sin rastrear
./scripts/stop-dev.ps1 -Wipe       # otra vez bases de datos vacías y un grafo vacío
```

## Si algo sale mal en el escenario

| Síntoma | Solución |
|---|---|
| `start-dev.ps1` dice que los puertos están ocupados | Dice qué contenedor o proceso es. Detenlo y vuelve a correrlo. |
| Compañeros / listas de curso vacías después de arrancar desde cero | student-service cargó el seed antes de que matching-service creara su cola. `./scripts/stop-dev.ps1 -Wipe` y arranca otra vez. |
| `kg …` imprime `[knowledge-graph] STALE` | `kg ingest resync`, luego `kg ingest embed`. |
| `ingest run` lista algo como UNBOUND | Agrega su nombre como alias en `knowledge-graph/capabilities.yaml` y vuelve a correrlo. (Vale la pena mostrarlo una vez: es el grafo negándose a adivinar.) |
| El agente no tiene la herramienta `kg_query` | El exe del MCP no estaba compilado cuando arrancó la sesión. La CLI (`kg q query …`) hace lo mismo. |
| El build falla con "file is being used by another process" | Una ventana de servicio o una sesión con el MCP `kg` tiene tomada la dll. Ciérrala y vuelve a compilar. |
| Neo4j está abajo | Cada skill lo dice y sigue adelante; corre los pasos del grafo después. |
