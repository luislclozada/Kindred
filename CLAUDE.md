# Kindred

A study group matcher: students register, add their courses, and see who else takes them.
Deliberately small — two services, a gateway, one frontend — and built so an agent can work on
it with a long-term memory: a knowledge graph of this repo that is consulted before code is
written and updated when work ships.

## Map

| Thing | Where | Port |
|---|---|---|
| student-service (module Students) | `src/Services/StudentService/` | 5101 |
| matching-service (module Matching) | `src/Services/MatchingService/` | 5102 |
| gateway (YARP) | `src/Gateway/Kindred.Gateway/` | 5000 |
| shared kernel | `src/SharedKernel/Kindred.SharedKernel/` | — |
| frontend (Angular) | `apps/frontend/` | 4200 (proxies `/api` → 5000) |
| knowledge graph tool | `tools/Kindred.KnowledgeGraph/` | — |
| specs and changes | `openspec/specs/`, `openspec/changes/` | — |
| capability spine | `knowledge-graph/capabilities.yaml` | — |
| PostgreSQL · RabbitMQ · Neo4j | `docker-compose.dev.yml` (Neo4j under profile `kg`) | 5432 · 5672/15672 · 7474/7687 |

Each service is three projects: `Kindred.<X>Service` (domain + application),
`Kindred.<X>Service.Data` (EF Core), `Kindred.<X>Service.Api` (Minimal APIs).

## Commands

```powershell
./scripts/start-dev.ps1 -Kg        # infra + services + frontend (+ Neo4j); stop with ./scripts/stop-dev.ps1
dotnet build Kindred.slnx
dotnet test tests/Kindred.StudentService.UnitTests
npm run build --prefix apps/frontend
dotnet ef migrations add <Name> -p src/Services/<X>Service/Kindred.<X>Service.Data -s src/Services/<X>Service/Kindred.<X>Service.Api

# knowledge graph (see the kg skill)
dotnet run --project tools/Kindred.KnowledgeGraph -- ingest run      # then: ingest embed
dotnet run --project tools/Kindred.KnowledgeGraph -- q query "<pregunta en español>" [--kind existence|blast]
dotnet run --project tools/Kindred.KnowledgeGraph -- ingest resync   # after commits; then: ingest embed
```

Start order matters: matching-service before student-service. The consumer declares its
queue; student-service publishes enrollment events while it seeds, and events published
before the queue exists are lost.

## Rules

Backend
1. **Business logic lives in aggregates.** Handlers orchestrate: load, ask the aggregate, save, publish.
2. **The domain depends on nothing** but the shared kernel. Repository interfaces in the domain, implementations in `.Data`.
3. **The mediator is in-house** (`Kindred.SharedKernel.Mediator`). Never MediatR.
4. **Expected failures are `Result` / `Error`** (Validation, NotFound, Conflict), not exceptions.
5. **Never expose an entity through the API** — DTOs only. Success returns the raw DTO; errors return `ApiError { code, message }` with 400 / 404 / 409.
6. **Never write around an aggregate** — no `DbSet.Update()`, no raw SQL writes.
7. **Services talk over RabbitMQ, never HTTP to each other.** Integration event contracts live in `Kindred.SharedKernel/Messaging/Events/`.
8. **Keep the event shape the graph reads**: a publisher constructs the event inline (`new XIntegrationEvent(...)`) in the handler file that uses `IEventBus`, after the save; a consumer implements `IIntegrationEventHandler<XIntegrationEvent>` and is idempotent.
9. **Gateway cluster ids equal the target service's kebab name** (`student-service`, `matching-service`).
10. Enums serialize SCREAMING_SNAKE_CASE. Time comes from `TimeProvider`.
11. Schema changes are EF migrations; services apply them at startup.

Frontend
12. Standalone, OnPush, signals, `@if` / `@for`. **Separate `.ts` / `.html` / `.scss`** — never inline templates or styles.
13. **Page → facade → api service.** A page never touches `HttpClient`.
14. **Shared primitives and tokens only** (`src/styles/`). No raw colours in component SCSS; dark mode comes from the tokens. Load the `new-module-recipe` skill before adding a page.

Process
15. **Commits are the user's call.** Ask before committing; never push. Agents dispatched by a skill never commit.
16. **Keep it small.** Every extra service, page or abstraction is something to explain. Build what the spec says and stop.
17. No auth in v1: the UI has a "sign in as" picker; the API takes the student id in the route.
18. **Spanish for people, English for code** — see "Language" below.

## Language

Kindred's users and readers are Spanish speakers (Mexico). Two languages, split by who reads the text:

| Spanish (es-MX, "tú") | English |
|---|---|
| UI text, API error **messages**, seed data | Code, identifiers, comments, file and folder names |
| `docs/**`, `knowledge-graph/README.md` | API paths, JSON fields, error **codes**, enum values, event names |
| Spec and change prose (purpose, requirement and scenario titles, proposal, design, tasks) | OpenSpec structure: `## Purpose`, `### Requirement:`, `#### Scenario:`, `**WHEN**` / `**THEN**`, and the keyword `MUST` |
| `name` and `description` in the capability spine | Capability ids and aliases, spec and change names, skills, agents, this file |
| **Questions put to the graph** | Commands and flags |

**Ask the graph in Spanish.** Its prose is Spanish, and the bundled embedding model is small:
a question matches a description best when the two share words. A Spanish question lands on
the capability and the spec that describe the thing; an English one only catches the English
identifiers left in the text. Build questions from the nouns below so they share words with the
specs — `q query "¿existe la formación de grupos de estudio?"`, not `"does group matching exist?"`.
Service and event names can be named as they are (`matching-service`).

Every requirement keeps an uppercase `MUST` inside a Spanish sentence
("El sistema MUST rechazar…") — `openspec validate --strict` rejects "debe".

Use these words, always the same ones:

| English | Spanish | | English | Spanish |
|---|---|---|---|---|
| student | estudiante | | classmates | compañeros |
| program (degree) | carrera | | shared courses | cursos en común |
| email | correo | | study group | grupo de estudio |
| course catalog | catálogo de cursos | | study group matching | formación de grupos de estudio |
| enrollment / enroll | inscripción / inscribirse | | course roster | lista del curso |
| drop (a course) | dar de baja | | availability | disponibilidad |
| sign in as | entrar como | | knowledge graph | grafo de conocimiento |
| capability | capacidad | | blast radius | radio de impacto |

## The knowledge graph lifecycle

The graph (Neo4j, built by `tools/Kindred.KnowledgeGraph`) joins specs, changes, docs and code
through the capability spine. It answers **existence** ("does X already exist?") and
**blast radius** ("what touches Y?") with `sourceFile@commit` citations. It is advisory
context — specs and tests are the enforcement layer. Details: `knowledge-graph/README.md`.

Work goes through these skills, which run the graph steps at the right moment:

| Moment | Use | Graph step |
|---|---|---|
| Thinking about an idea | `/kindred-explore` | Query for cited current state first |
| Starting a change | `/kindred-propose <name>` | Existence gate **before** creating anything → OpenSpec change → register it in the spine → `ingest run` (in-flight work is queryable immediately) |
| Building it | `/kindred-apply` | Blast radius → file-ownership matrix → parallel teams → wave gate (build + tests) |
| Shipping it | `/kindred-commit-n-archive` | Archive → commit (with your approval) → `ingest resync` + `ingest embed` |
| Any question about what exists | `kg` skill / `kg_query` MCP tool | — |

Before building anything that might already exist, ask the graph. If a query prints
`[knowledge-graph] STALE`, run `ingest resync` then `ingest embed` and ask again.

The capability spine (`knowledge-graph/capabilities.yaml`) is the only place capability ids
are minted. If `ingest run` reports something as UNBOUND, add an alias there and re-run.

The `kg` MCP server runs the **built exe** (`.mcp.json`). Build the tool once before first
use, and close sessions using the server before rebuilding `tools/Kindred.KnowledgeGraph`.
