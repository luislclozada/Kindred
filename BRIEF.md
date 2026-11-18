# Kindred — handoff brief for Claude Code

> Read this whole file before touching anything. It carries every decision made so far, what
> is already written, and what is left, in build order. When it disagrees with your instincts,
> follow the brief and flag the disagreement to Luis rather than silently diverging.

## 1. Why this project exists

Luis is giving a university talk titled **"Your AI Has Amnesia. Let's Fix That."** to students
(date: around Thu Nov 19, 2026 — confirm with Luis; the scope below assumes days, not weeks).
Goal: leave students inspired by showing what they can build **today** with AI agents that have
a long-term memory, and how that skill opens doors.

The memory is a **Neo4j knowledge graph ("second brain")** of the repo itself, built and kept
true by a deterministic .NET tool, and read by agents through skills/MCP before they write code.
The pattern already exists in Luis's BonMotz repo (`C:\Projects\LANGUAGE-LEARNING`). BonMotz is
**not** shown in the talk — Kindred is a small, fresh app that replicates the pattern so students
are not overwhelmed.

### The app: Kindred — a study group matcher

- **v1 (exists before the talk):** students register, add their courses, and see classmates —
  who else takes each of their courses, ranked by number of shared courses.
- **Live during the talk:** add a feature with the agent workflow, e.g.
  `add-availability-and-group-matching` — students declare weekly availability (student-service),
  matching-service forms study groups by course + overlapping availability, the frontend gets a
  "My groups" page. It spans both services + frontend, so the graph's blast-radius check and the
  parallel apply (3 teams) are both visible on stage.

### Demo story (what the audience sees)

1. Quick tour of the running app (Angular, polished, Higgsfield visuals).
2. "The AI has amnesia": a fresh agent session knows nothing about the repo.
3. Show the graph: `ingest run` + `ingest embed`, then `q query "does group matching exist?"` →
   cited answer (nothing yet), and `q query "what touches student enrollment?" --kind blast` →
   student-service → `StudentEnrolledInCourseIntegrationEvent` → matching-service.
4. `/kindred-propose add-availability-and-group-matching` → existence gate → OpenSpec change →
   spine registration → `ingest run` → the change is visible to existence queries immediately
   (IN_FLIGHT spec node).
5. `/kindred-apply` → plan the split from the blast radius → 3 parallel teams → wave gate.
6. `/kindred-commit-n-archive` → archive, commit, `ingest resync` → the graph now remembers.

## 2. Decisions already made (do not reopen without Luis)

| Topic | Decision |
|---|---|
| Name / prefix | **Kindred** → namespaces `Kindred.*`, skills `kindred-*`, KG tool `Kindred.KnowledgeGraph` |
| Backend | .NET 10, **2 services + gateway**: `student-service`, `matching-service`, `gateway` (YARP) |
| Architecture | Same as BonMotz: DDD aggregates, CQRS, Minimal APIs, EF Core + PostgreSQL, **in-house mediator (never MediatR)**, services talk over **RabbitMQ**, never HTTP to each other |
| KG tool | **Slim port** of `Bonmotz.KnowledgeGraph` (done — see §4) |
| Frontend | **Angular** (standalone, OnPush, signals), polished brutalist-ish look, with **Higgsfield-generated assets** (hero/illustrations) |
| Skills | BonMotz orchestration skills copied and renamed `bonmotz-*` → `kindred-*`; `openspec-*` copied verbatim |
| Auth | None in v1 (BonMotz uses Keycloak — out of scope for the demo). A "sign in as" student picker in the UI; the API takes the student id in the route |

## 3. Ports and runtime

| Thing | Port |
|---|---|
| gateway | 5000 |
| student-service | 5101 |
| matching-service | 5102 |
| Angular dev server | 4200 (proxy `/api` → 5000) |
| PostgreSQL | 5432 (dbs `kindred_students`, `kindred_matching`; user `kindred` / `kindred_dev`) |
| RabbitMQ | 5672, UI 15672 (`kindred` / `kindred_dev`) |
| Neo4j | 7474 / 7687 (`neo4j` / `kindred_kg_dev`), compose profile `kg` |

Start order for dev (put it in `scripts/start-dev.ps1`): `docker compose -f docker-compose.dev.yml up -d`
(+ `--profile kg` for Neo4j) → matching-service → student-service → gateway → frontend.
Matching must start before student-service seeds, because the consumer declares its queue
(publishers only declare the exchange).

## 4. What is already written (in this folder)

```
Kindred/
├─ BRIEF.md                      ← this file
├─ Directory.Build.props         net10.0, nullable, implicit usings
├─ Directory.Packages.props      central package management (floating 10.0.* etc.) — csproj files carry names only
├─ tools/Kindred.KnowledgeGraph/ slim KG port — COMPLETE, NEVER COMPILED (see below)
└─ src/SharedKernel/Kindred.SharedKernel/
   ├─ Domain/Entity.cs, Result.cs          Entity, AggregateRoot (domain events), Result/Error
   ├─ Mediator/Abstractions.cs, Mediator.cs ICommand/IQuery/handlers, IMediator, AddKindredMediator(assemblies)
   └─ Messaging/                            IIntegrationEvent, IEventBus, IIntegrationEventHandler<T>,
                                            RabbitMqEventBus, RabbitMqConsumerService, AddKindredMessaging,
                                            AddIntegrationEventHandler<TEvent,THandler>,
                                            Events/EnrollmentEvents.cs (StudentEnrolledInCourse / StudentDroppedCourse)
```

**Nothing has been compiled.** NuGet was blocked in the cloud session. Your first job is to make
it build (§6 step 1). Expect a handful of small API mismatches (RabbitMQ.Client 7 async API,
System.CommandLine beta6, ModelContextProtocol 2.0) — fix them, don't redesign.

### KG tool: what changed vs Bonmotz.KnowledgeGraph

Kept as-is (namespace renamed): ontology/provenance/gate design, Neo4j single write path,
reversible batches + tombstones, Tier C import (PROPOSED hold), hybrid retrieval, duplicate
detection, review queue, rollback/validate/schema, MCP server (`graph mcp`: `kg_query`, `kg_stats`),
freshness probe (commit distance only).

Dropped: Railway/deploy/release tracking, route-contract manifests, Angular/SCSS/Storybook
inventory (Library/Component/Primitive/TokenSet), acceptance command, hardcoded BonMotz bridges.

Changed / added:
- Repo root marker is `Kindred.slnx`. Code roots scanned: `src/`, `tools/`. `ServiceKeyFor`
  strips `Kindred.` and `.Api/.Data/.Tests/.UnitTests/.IntegrationTests`.
- **Integration events from code (Tier A):** a type implementing `IIntegrationEvent` → `DomainEvent`
  node; a file that uses `IEventBus` and does `new XEvent(` → `service -[PUBLISHES]-> event`;
  `IIntegrationEventHandler<XEvent>` → `event -[CONSUMED_BY]-> service`. Owner = nearest csproj.
  **So publishers must construct the event inline in a file that references `IEventBus`**, and
  handlers must implement `IIntegrationEventHandler<T>` — keep that convention.
- **Gateway routes (Tier B):** `src/Gateway/Kindred.Gateway/appsettings.json` `ReverseProxy.Routes`
  → `service:gateway -[CALLS {routes}]-> service:<ClusterId>`. **Cluster ids must equal the
  target service's kebab name** (`student-service`, `matching-service`). Stale edges are retracted.
- **Ports** read from each project's `Properties/launchSettings.json`.
- **Bridges declared in the spine** (`bridges:` in `capabilities.yaml`), not hardcoded.
- **Change nodes** carry `path` (proposal.md, embedded) and `tasksDone/tasksTotal`.
- **In-flight deltas:** `openspec/changes/<c>/specs/<s>/spec.md` → `spec:<s> -[INTRODUCED_BY {delta}]-> change:<c>`;
  if `<s>` has no main spec yet, an `IN_FLIGHT` Spec node is minted (archive turns it CURRENT).
- Seedable (embedded) labels: Doc, Spec, Feature, Change, Capability, DomainEvent. Service nodes
  are lexically seeded from names in the graph (no hardcoded list).
- Spine binder also binds `Service` nodes by name.
- Default Neo4j password `kindred_kg_dev`.

## 5. Domain model for v1

**student-service** (module `Students`)
- `Course` aggregate: `CourseCode` VO (e.g. `COMP-248`, normalized upper-case, `^[A-Z]{3,4}-\d{3}$`),
  `Title`, `Department`. Read-only catalog, seeded (≈8 courses: COMP-248, COMP-249, COMP-352,
  MATH-203, MATH-205, PHYS-204, SOEN-287, ENGL-212).
- `Student` aggregate: `Id` (Guid), `DisplayName`, `Email` (unique), `Program`, `Enrollments`
  (entity: `CourseCode`, `EnrolledAt`). Rules: no duplicate enrollment (Conflict), max 6 courses
  (Validation), drop only an enrolled course (NotFound). Raises domain events
  `StudentEnrolledInCourse` / `StudentDroppedCourse`.
- Command handlers save, then map domain events → integration events and publish via `IEventBus`
  **inline in the handler file** (so the KG sees PUBLISHES).
- Commands: `RegisterStudent`, `EnrollInCourse`, `DropCourse`. Queries: `ListCourses` (with
  enrolled count), `ListStudents`, `GetStudent`, `GetClassmates` (other students sharing ≥1 course,
  with `sharedCourses[]`, sorted by shared count desc).
- Endpoints (`/api/v1`): `POST /students`, `GET /students`, `GET /students/{id}`,
  `POST /students/{id}/enrollments {courseCode}`, `DELETE /students/{id}/enrollments/{courseCode}`,
  `GET /students/{id}/classmates`, `GET /courses`.
- Seed on startup in Development if empty: catalog + ~12 demo students with overlapping
  enrollments, **through the mediator commands** so events are published.

**matching-service** (module `Matching`)
- v1 only keeps a `CourseRoster` read model (course code → student ids/names) from
  `StudentEnrolledInCourse` / `StudentDroppedCourse` handlers (`IIntegrationEventHandler<T>`).
- Endpoint: `GET /api/v1/matching/courses/{courseCode}/roster`.
- Group matching itself is the **live feature** — do NOT build it in v1.

**Conventions for both services** (BonMotz rules, keep them):
- Projects per service: `Kindred.<X>Service` (domain + application: aggregates, VOs, events,
  repository interfaces, commands/queries/handlers, DTOs), `Kindred.<X>Service.Data` (DbContext,
  EF configurations, repositories, design-time factory), `Kindred.<X>Service.Api` (Program.cs,
  Minimal API endpoint groups, launchSettings).
- Business logic in aggregates; handlers orchestrate. Domain depends on nothing. Never expose an
  entity through the API — DTOs only. Never `DbSet.Update()` around an aggregate.
- Success responses return **raw DTOs**; errors return a small `ApiError { code, message }`
  with 400/404/409 mapped from `ErrorType`.
- Enums serialize SCREAMING_SNAKE_CASE (`JsonNamingPolicy.SnakeCaseUpper`).
- Persistence: migrations once it builds (`dotnet ef migrations add InitialCreate -p <Data> -s <Api>`);
  at startup `Migrate()` if migrations exist, else `EnsureCreated()`.
- Layout: `src/Services/StudentService/...`, `src/Services/MatchingService/...`,
  `src/Gateway/Kindred.Gateway/`, `tests/Kindred.StudentService.UnitTests/` (xUnit, aggregate rules).
- Gateway: YARP routes `/api/v1/students/{**rest}` + `/api/v1/courses/{**rest}` → `student-service`,
  `/api/v1/matching/{**rest}` → `matching-service`; CORS for `http://localhost:4200`.

## 6. Remaining work, in order

1. **Make it build.** Create `Kindred.slnx` (all projects incl. the KG tool), build, fix errors.
2. **Services + gateway** per §5, plus `docker-compose.dev.yml` (postgres with both dbs via an
   init script, rabbitmq:management, neo4j under profile `kg` with the APOC-free default image)
   and `scripts/start-dev.ps1`. Run them; hit every endpoint through the gateway.
3. **OpenSpec + spine.** `openspec init` (or hand-create `openspec/config.yaml` modeled on
   BonMotz's — read `C:\Projects\LANGUAGE-LEARNING\openspec\config.yaml`). Main specs for v1 under
   `openspec/specs/`: `student-profiles`, `course-catalog`, `course-enrollment`,
   `classmate-discovery`, `course-rosters`, `api-gateway`. Write `knowledge-graph/capabilities.yaml`:

   ```yaml
   capabilities:
     - id: student-profiles
       name: Student profiles
       description: Students register with a display name, email and program.
       module: Students
       aliases: [student-service, student-*]
     - id: course-enrollment
       name: Course catalog and enrollment
       description: The course catalog, and students enrolling in or dropping courses.
       module: Students
       aliases: [course-catalog, course-enrollment, enrollment-*]
     - id: classmate-discovery
       name: Classmate discovery
       description: See who else takes your courses, ranked by shared courses.
       module: Students
       aliases: [classmate-*]
     - id: study-group-matching
       name: Study group matching
       description: Course rosters today; forming study groups by course and availability next.
       module: Matching
       aliases: [matching-service, course-rosters, matching-*, group-*, availability-*]
     - id: platform
       name: Platform
       description: Gateway, shared kernel, messaging and the knowledge graph tool.
       module: Platform
       aliases: [gateway, shared-kernel, knowledge-graph, api-gateway]
   bridges:
     - from: Matching
       to: Students
       via: student-enrolled-in-course
       kind: domain-event
       status: active
   ```
   Then `ingest run` → **0 UNBOUND**, `ingest embed` → no zero-coverage label, and sanity queries:
   `q query "does classmate discovery exist?"`, `q query "what touches enrollment?" --kind blast`.
   Also add `docs/FEATURES.md` + `docs/features/classmate-discovery/README.md` (short) and a
   `knowledge-graph/README.md` (adapt the BonMotz tool README's run/lifecycle sections).
4. **Skills** into `.claude/skills/`, from `C:\Projects\LANGUAGE-LEARNING\.claude\skills\`:
   - `bonmotz-explore|propose|apply|commit-n-archive` → `kindred-*`; `kg` → `kg`; adapt every path,
     command (`dotnet run --project tools/Kindred.KnowledgeGraph -- …`), compose command, doc
     references and agent names. Remove BonMotz-only steps (lesson identity gate, content-leak
     guard, Railway, RLS, Keycloak, compliance docs, `lang-learn-*` → use plain git commit with
     user approval, Nx). Keep every KG step and every guardrail (one writer per file, wave gate,
     no commits by agents, disjointness pre-flight).
   - `kindred-apply/references/agent-briefing.md` → adapt to .NET slice order + Angular rules here.
   - `new-module-recipe` → rewrite for Kindred's own design system (once the frontend exists).
   - `openspec-*` → copy verbatim.
   - Agents: copy from `C:\Projects\LANGUAGE-LEARNING\.claude\agents\` the ones the apply skill
     dispatches (clean-code-architect, angular-frontend-architect, test-architect), strip BonMotz
     specifics.
   - `.mcp.json`: register `kg` → the **built exe** (`tools/Kindred.KnowledgeGraph/bin/Debug/net10.0/Kindred.KnowledgeGraph.exe graph mcp`),
     not `dotnet run` (a running server locks the dll and `dotnet run` rebuild fails — BonMotz lesson).
   - `CLAUDE.md` with the rules from §5 and the KG lifecycle.
5. **Angular frontend** `apps/frontend` (Angular 20+, standalone, OnPush, signals, separate
   .ts/.html/.scss, semantic CSS tokens, dark mode via CSS vars): pages "Sign in as" (student
   picker + register), "My courses" (catalog, enroll/drop), "Classmates" (grouped by course, shared
   count badges). Facade → service pattern. Leave clearly marked slots for Higgsfield images
   (`src/assets/brand/`). Make it look good — this is what hooks the students.
6. **Rehearse the demo path** end to end (§1 demo story) on a clean graph, commit v1, and write
   `docs/DEMO.md` with the exact commands in order. Prepare the live change prompt but do **not**
   create the change folder — that happens on stage.

## 7. Guardrails

- Commits are Luis's call — ask before committing; never push.
- Keep it small. Every extra service, page or abstraction is something to explain on stage.
- If NuGet versions float to something incompatible, pin them in `Directory.Packages.props`.
- BonMotz repo is read-only reference material. Never write there.
