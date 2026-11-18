# El grafo de conocimiento de Kindred

Un grafo de este repo con procedencia en cada hecho — su "segundo cerebro" — guardado en Neo4j y
construido por `tools/Kindred.KnowledgeGraph`. Une cuatro cosas que de otro modo nunca se
encuentran (las specs de OpenSpec, los cambios de OpenSpec, los docs y el código .NET) a través
de una **columna de capacidades** (*capability spine*) declarada a mano, y responde dos tipos de
pregunta con citas:

- **Existencia** — "¿ya existe la formación de grupos de estudio?"
- **Radio de impacto** — "¿qué toca la inscripción a cursos?"

> **Contexto de apoyo, no un oráculo de correctitud.** Cada hecho lleva `sourceFile@commit`.
> Las specs y las pruebas siguen siendo la capa que hace cumplir las reglas.

## Córrelo

```powershell
# 1. Neo4j (perfil opcional de compose)
docker compose -f docker-compose.dev.yml --profile kg up -d neo4j

# 2. Compila la herramienta una vez (el servidor MCP corre el exe compilado)
dotnet build tools/Kindred.KnowledgeGraph

# 3. Ingiere el repo: hechos deterministas + lectura de convenciones, sin LLM
dotnet run --project tools/Kindred.KnowledgeGraph -- ingest run

# 4. Embeddings para la búsqueda: totalmente locales, modelo ONNX en proceso, sin key, sin costo
dotnet run --project tools/Kindred.KnowledgeGraph -- ingest embed

# 5. Pregunta
dotnet run --project tools/Kindred.KnowledgeGraph -- q query "¿ya existe descubrir compañeros con cursos en común?"
dotnet run --project tools/Kindred.KnowledgeGraph -- q query "¿qué toca la inscripción?" --kind blast
dotnet run --project tools/Kindred.KnowledgeGraph -- q detect-duplicates
dotnet run --project tools/Kindred.KnowledgeGraph -- q stats
```

Pregúntale en español: el texto del grafo (specs, docs, la columna de capacidades) está en
español; el código, los comandos y los identificadores se quedan en inglés. El modelo de
embeddings incluido es pequeño: encuentra mejor una descripción cuando la pregunta comparte
palabras con ella. Una pregunta en inglés solo alcanza los identificadores en inglés que quedan
en el texto; una en español llega a la capacidad y a la spec que describen lo que buscas.

La herramienta lee el estado con commit del repo (`git ls-files`, `git rev-parse HEAD`), así que
necesita un repositorio git con al menos un commit.

Configuración: `NEO4J_URI` (por defecto `bolt://localhost:7687`), `NEO4J_USER` (`neo4j`),
`NEO4J_PASSWORD` (`kindred_kg_dev`), desde el entorno o un archivo `.env`. Ninguna API key de
ningún tipo.

## Qué hay en el grafo

| Nodo | Llave | De dónde sale | Nivel |
|---|---|---|---|
| `Service` | `service:student-service` | cada `.csproj` bajo `src/` y `tools/` (`Kindred.StudentService{,.Api,.Data}` se juntan en uno) + el puerto de `launchSettings.json` | A |
| `DomainEvent` | `event:StudentEnrolledInCourseIntegrationEvent` | un tipo que implementa `IIntegrationEvent` | A |
| `Spec` | `spec:course-enrollment` | `openspec/specs/<name>/spec.md` (`authority: CURRENT`), o un delta en un cambio activo para una spec que todavía no existe (`authority: IN_FLIGHT`) | A / B |
| `Change` | `change:<name>` | `openspec/changes/<name>/` y `archive/<date>-<name>/`, con `tasksDone/tasksTotal` | A |
| `Doc`, `Feature` | `doc:docs/FEATURES.md`, `feature:classmate-discovery` | `docs/**/*.md`, `docs/features/<name>/`, la gramática de entradas de `FEATURES.md` | A / B |
| `Capability`, `Module` | `capability:study-group-matching`, `module:Matching` | **solo** `knowledge-graph/capabilities.yaml` | B |

| Arista | Significa | De dónde sale |
|---|---|---|
| `DEPENDS_ON` | servicio → servicio al que hace referencia | `<ProjectReference>` |
| `PUBLISHES` | servicio → evento | `new XIntegrationEvent(` en un archivo que usa `IEventBus` |
| `CONSUMED_BY` | evento → servicio | una clase que implementa `IIntegrationEventHandler<XIntegrationEvent>` |
| `CALLS` | gateway → servicio, con las rutas | `ReverseProxy.Routes` en el `appsettings.json` del gateway (id del cluster = nombre del servicio) |
| `BOUND_TO` | spec / cambio / feature / servicio → capacidad | los ids y alias de la columna de capacidades |
| `OWNED_BY` | capacidad → módulo | la columna de capacidades |
| `BRIDGES` | unión módulo ↔ módulo | `bridges:` en la columna de capacidades |
| `INTRODUCED_BY` | spec → cambio (`delta: ADDED \| MODIFIED`) | `openspec/changes/<c>/specs/<s>/spec.md` |
| `DOCUMENTED_BY`, `REFERENCES`, `SUPERSEDED_BY` | estructura de los docs | carpetas de `docs/`, links de markdown, `FEATURES.md` |

Como los publicadores y los consumidores se leen de la forma del código, respeta la convención:
construye el evento de integración inline en el archivo del handler que usa `IEventBus`, e
implementa `IIntegrationEventHandler<T>` del lado que consume.

## Cómo se mantiene confiable

- **Un solo camino de escritura** — cada nodo y arista lleva el sello `{sourceFile, commitHash,
  extractorTier, batchId, extractedAt, extractor}`. Una escritura sin sello lanza una excepción;
  `graph validate` audita el grafo vivo.
- **Nivel de confianza = ruta de riesgo** — el Nivel A (hechos del repo) y el Nivel B
  (gramáticas de convención) escriben directo. El Nivel C (aristas que un agente extrajo de
  prosa, `ingest import-edges --file <json>`) se queda como `PROPOSED` hasta que una persona
  corre `review approve`.
- **Capa de consistencia** — un link de un doc a un archivo que no existe se escribe como
  `DISPUTED: target-missing`; una dependencia que afirma un doc sin un hecho del repo que la
  respalde queda como `DISPUTED: uncorroborated-by-repo-facts`. Un doc equivocado no puede
  volverse verdad en silencio.
- **La columna de capacidades** (`capabilities.yaml`) es el único lugar donde se crean ids de
  capacidad. Las fuentes que no se ligan a nada se reportan como UNBOUND en cada corrida:
  amplía los alias a mano y vuelve a ingerir.
- **Lotes reversibles** — `graph rollback --batch <id> [--dry-run]`. `ingest resync` se limita
  al `git diff` desde el último commit ingerido y marca como borrados (tombstones) los archivos
  eliminados.
- **Cero proveedores de LLM** — los embeddings son un modelo ONNX en proceso
  (`SmartComponents.LocalEmbeddings`, 384 dimensiones). La herramienta busca y cita; el *agente
  que la llama* escribe la respuesta, siguiendo el contrato de la skill `kg`.

`graph schema` regenera `knowledge-graph/schema-reference.md` a partir de la ontología en C#.

## El ciclo de vida — cómo crece el grafo

Al grafo lo mantiene al día el ciclo de vida de los cambios, no un proceso en segundo plano:

| Momento | Skill | Qué le pasa al grafo |
|---|---|---|
| Explorar una idea | `kindred-explore` | Se consulta el estado actual, con citas, antes de empezar a pensar |
| Proponer un cambio | `kindred-propose` | **Compuerta de existencia** antes de crear nada; luego el cambio se registra en la columna de capacidades y `ingest run` lo liga: el trabajo en curso es visible para las consultas de existencia desde el primer día (una spec completamente nueva aparece como `IN_FLIGHT`) |
| Implementar | `kindred-apply` | El **radio de impacto** decide cómo se reparte el trabajo entre equipos en paralelo |
| Archivar + commit | `kindred-commit-n-archive` | Después del commit, `ingest resync` + `ingest embed` registran la nueva verdad: el cambio queda archivado y sus specs son `CURRENT` |

Red de seguridad: cada consulta compara el último commit ingerido del grafo con `HEAD` e
imprime `[knowledge-graph] STALE: graph is N commits behind HEAD` cuando no coinciden. El
remedio es `ingest resync` y luego `ingest embed`.

Respaldo: `openspec/config.yaml` exige que las propuestas citen la revisión de existencia y que
las tareas incluyan el registro en la columna de capacidades, aunque se usen directamente las
skills `openspec-*`.

## Conexión con Claude Code

- **Skill** `.claude/skills/kg/`: la tabla de comandos y el contrato para armar la respuesta.
- **MCP**: registrado como `kg` en `.mcp.json` (`graph mcp`, stdio). Herramientas: `kg_query`,
  `kg_stats`. El mismo motor y las mismas citas que la CLI.

  **`.mcp.json` lanza el exe compilado, no `dotnet run`; no cambies eso.** `dotnet run`
  recompila en cada arranque, y el build falla siempre que un servidor en ejecución ya tiene
  tomada la dll. Consecuencias: compila la herramienta una vez antes del primer uso, y detén
  todos los servidores `kg` en ejecución (cierra las sesiones de Claude Code que los usan) antes
  de recompilar la herramienta.

## Límites conocidos

- El grafo indexa specs, cambios, docs y hechos de código a nivel de proyecto, no el interior
  del código fuente.
- Neo4j community no tiene restricciones de existencia de propiedades; en su lugar, el camino de
  escritura y `graph validate` hacen cumplir la procedencia.
- La revisión humana del Nivel C es una CLI (`review list | approve | reject`), no una UI.
