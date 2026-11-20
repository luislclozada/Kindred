# Kindred

Un formador de grupos de estudio: los estudiantes se registran, agregan sus cursos y ven quién
más los toma. Es pequeño a propósito — dos servicios, un gateway y un frontend — y está hecho
para que un agente trabaje en él con memoria de largo plazo: un grafo de conocimiento del repo
que se consulta antes de escribir código y se actualiza cuando el trabajo sale.

## Requisitos

Para correr la app solo hacen falta dos cosas:

- **Docker** (Docker Desktop en Windows), con `docker compose`
- **GNU make** (en Windows: `choco install make`)

Para desarrollar (compilar, correr pruebas, hot reload, el grafo de conocimiento) también:

- **.NET SDK 10**
- **Node.js 24** con npm 11
- **PowerShell**, para los scripts de `scripts/` (solo Windows)

## Arranque rápido: todo en Docker

*run it with bash*

```powershell
make kindred-start
```

Construye las imágenes y levanta PostgreSQL, RabbitMQ, Neo4j, los dos servicios, el gateway y el frontend. La primera vez tarda unos minutos; el comando regresa cuando todo está sano.

| Qué | Dónde | Credenciales |
|---|---|---|
| App | http://localhost:11200 | — (elige un estudiante en "entrar como") |
| Gateway | http://localhost:11000/api/v1/courses | — |
| UI de RabbitMQ | http://localhost:11673 | `kindred` / `kindred_dev` |
| Navegador de Neo4j | http://localhost:11474 | `neo4j` / `kindred_kg_dev` |

La app arranca con datos de prueba: doce estudiantes ya inscritos en cursos.

Para detenerla:

```powershell
make kindred-stop            # detiene todo y conserva los datos
make kindred-stop WIPE=1     # además borra las bases de datos, las colas y el grafo
```

Los contenedores tienen el código como estaba cuando se construyeron. Después de cambiar
código, corre `make kindred-start` otra vez: reconstruye y reinicia lo que cambió.

Sin make, el comando equivalente es:

```powershell
docker compose -f docker-compose.dev.yml --profile kg --profile app up -d --build --wait
docker compose -f docker-compose.dev.yml --profile kg --profile app down    # agrega -v para borrar los datos
```

## Desarrollo: la app en tu máquina, con hot reload

La infraestructura sigue en Docker; los servicios y el frontend corren en tu máquina, cada uno
en su propia ventana.

```powershell
npm install --prefix apps/frontend     # una vez
./scripts/start-dev.ps1 -Kg            # -Kg agrega Neo4j; -NoFrontend omite el frontend
./scripts/stop-dev.ps1                 # -Wipe también borra todos los datos
```

Las URLs son las mismas que en Docker. Este modo y `make kindred-start` usan los mismos
puertos: detén uno antes de arrancar el otro.

El script arranca matching-service **antes** que student-service, y el orden importa:
student-service publica eventos de inscripción mientras carga los datos de prueba, y los
eventos publicados antes de que matching-service cree su cola se pierden. Si arrancas los
servicios a mano, respeta ese orden.

## Compilar y probar

```powershell
dotnet build Kindred.slnx
dotnet test tests/Kindred.StudentService.UnitTests
npm run build --prefix apps/frontend
```

Los cambios de esquema son migraciones de EF Core; cada servicio las aplica al arrancar.

```powershell
dotnet ef migrations add <Name> -p src/Services/<X>Service/Kindred.<X>Service.Data -s src/Services/<X>Service/Kindred.<X>Service.Api
```

## Puertos

Todos están en los 11000 para no chocar con otro stack: `11` + los últimos tres dígitos del
puerto habitual (5432 → 11432). La única excepción es la UI de RabbitMQ, 15672 → 11673.

| Componente | Carpeta | Puerto |
|---|---|---|
| Frontend (Angular) | `apps/frontend/` | 11200 |
| Gateway (YARP) | `src/Gateway/Kindred.Gateway/` | 11000 |
| student-service | `src/Services/StudentService/` | 11101 |
| matching-service | `src/Services/MatchingService/` | 11102 |
| PostgreSQL | `docker-compose.dev.yml` | 11432 |
| RabbitMQ (AMQP · UI) | `docker-compose.dev.yml` | 11672 · 11673 |
| Neo4j (UI · Bolt) | `docker-compose.dev.yml` | 11474 · 11687 |

## El grafo de conocimiento

Neo4j tiene que estar arriba (`make kindred-start` o `./scripts/start-dev.ps1 -Kg`).

```powershell
dotnet build tools/Kindred.KnowledgeGraph                                # una vez
dotnet run --project tools/Kindred.KnowledgeGraph -- ingest run          # lee el repo, sin LLM
dotnet run --project tools/Kindred.KnowledgeGraph -- ingest embed        # embeddings locales, sin API key
dotnet run --project tools/Kindred.KnowledgeGraph -- q query "¿existe la formación de grupos de estudio?" --kind existence
dotnet run --project tools/Kindred.KnowledgeGraph -- q query "¿qué toca la inscripción a cursos?" --kind blast
```

Pregúntale en español: su texto está en español. Después de hacer commits, corre
`ingest resync` y luego `ingest embed`. Si una consulta imprime `[knowledge-graph] STALE`, el
remedio es el mismo.

El servidor MCP `kg` de Claude Code (`.mcp.json`) corre el exe ya compilado: compila la
herramienta antes de abrir la sesión y cierra las sesiones que lo usan antes de recompilarla.

Detalles: [knowledge-graph/README.md](knowledge-graph/README.md).

## Si algo sale mal

| Síntoma | Solución |
|---|---|
| "port is already allocated" al arrancar | Otro proceso tiene un puerto de los 11000. `docker ps` y `netstat -ano` dicen cuál; detenlo y vuelve a arrancar. |
| Falla al construir una imagen | El error de compilación sale en la terminal. `dotnet build Kindred.slnx` da el mismo error más rápido. |
| Un servicio no queda sano | `docker logs kindred-student-service` (o `kindred-matching-service`, `kindred-gateway`). |
| Compañeros o listas de curso vacías después de arrancar desde cero | student-service cargó los datos antes de que matching-service creara su cola. `make kindred-stop WIPE=1` y arranca otra vez. |
| El build falla con "file is being used by another process" | Una sesión con el MCP `kg`, o un servicio arrancado con `start-dev.ps1`, tiene tomado el archivo. Ciérralo y vuelve a compilar. |

## Más documentación

- [docs/FEATURES.md](docs/FEATURES.md) — las features que ya salieron
- [docs/DEMO.md](docs/DEMO.md) — el guion de la demo, paso a paso
- [apps/frontend/README.md](apps/frontend/README.md) — el patrón del frontend
- [knowledge-graph/README.md](knowledge-graph/README.md) — cómo funciona el grafo
- `openspec/specs/` — las specs: el contrato de cada feature
- [CLAUDE.md](CLAUDE.md) — las reglas del proyecto y el ciclo de vida de los cambios
