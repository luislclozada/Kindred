# Agent briefing templates

Fill every `<…>`. A briefing missing the **Owned paths** or **Do not** block must not be sent —
those two are what keep concurrent teams out of each other's files.

---

## Shared block — paste into every briefing

```
## Change: <change-name>  |  Team <n> — <slice name>  |  Wave <w>

### Context to read first (do not skip)
- openspec/changes/<change-name>/proposal.md
- openspec/changes/<change-name>/design.md
- openspec/changes/<change-name>/specs/<capability>/spec.md   ← your acceptance criteria
- CLAUDE.md — rules + anti-patterns
- openspec/config.yaml — `context:` and `rules:` are hard constraints
- <existing files selected in Step 2a — the closest existing slice is your template>
<KG findings, if any, with sourceFile@commit citations>

### Contracts already in place (written by the orchestrator in wave 0 — use, do not change)
- <integration event record(s) in src/SharedKernel/Kindred.SharedKernel/Messaging/Events/>
- <DTO / endpoint shapes from specs/**>

### Your tasks (from tasks.md — verbatim)
- <id> <text>
- <id> <text>

### Owned paths — you may create/edit ONLY inside these
- <path>
- <path>

### Do NOT touch (owned by another team or by the orchestrator)
- openspec/changes/<change-name>/tasks.md   ← never, by anyone but the orchestrator
- <shared file> — owned by Team <n>
- <shared file> — orchestrator-owned

### Rules
- Same working tree, current branch. NO git worktree, stash, checkout, reset, clean, or
  branch switch. NO commits, pushes, or PRs — commits are the user's call.
- Need an edit outside your owned paths? STOP and report it. Do not reach across.
- Do NOT run builds or tests (`dotnet build`, `dotnet test`, `dotnet ef`, `npm run build`) —
  the orchestrator gates each wave, and concurrent builds collide.
- No destructive commands: no `docker compose down -v`, no database drops, no row-deleting SQL,
  no `npm install`. They need the user's approval, which you cannot obtain.
- Spanish for people, English for code: UI text and API error messages are Mexican Spanish
  ("tú", glossary in CLAUDE.md "Language"); identifiers, comments, error codes, paths and log
  messages stay English.

### Report back
- Files created/modified (full paths)
- Task ids you consider done, and the evidence for each
- Anything you could NOT do and why (blocked, ambiguous, outside your paths)
- Anything the orchestrator must do at the gate (e.g. "EF model changed — needs a migration",
  "new handler needs registering in Program.cs" if Program.cs is not yours)
- Deviations from design.md / specs — flag them; do not silently improvise
```

---

## Backend — `clean-code-architect`

Append:

```
### .NET slice order (openspec/config.yaml rules.tasks — follow it)
domain (aggregate + value objects + domain events)
  → EF Core entity configuration   (NO migration — the orchestrator generates it at the gate)
  → command/query handler
  → repository (interface in the domain project, implementation in .Data)
  → Minimal API endpoint + DTOs
  → xUnit tests for the aggregate rules

### Where things go (per service X)
- src/Services/<X>Service/Kindred.<X>Service/Domain/        aggregates, value objects, domain events, errors, repository interfaces
- src/Services/<X>Service/Kindred.<X>Service/Application/   commands, queries, handlers, DTOs, integration event handlers
- src/Services/<X>Service/Kindred.<X>Service.Data/          DbContext, EF configurations, repositories
- src/Services/<X>Service/Kindred.<X>Service.Api/           Program.cs, Endpoints/

### Hard constraints
- Mediator: `Kindred.SharedKernel.Mediator` (ICommand / IQuery + handlers, discovered by
  AddKindredMediator). NEVER MediatR.
- Business logic lives in the aggregate. Handlers orchestrate; they do not decide.
- Expected failures are `Result` / `Error` values (Validation / NotFound / Conflict), not exceptions.
- Domain depends on nothing but the shared kernel. Repository interface in the domain,
  implementation in .Data.
- Never expose a domain entity through the API — always a DTO. Success = raw DTO
  (`ToHttpResult()` / `ToCreatedResult()`); failure = `ApiError { code, message }` with 400/404/409.
- Never write around an aggregate with `DbSet.Update()` or raw SQL.
- Enums serialize SCREAMING_SNAKE_CASE (already configured by `AddKindredJson()`).
- Integration events: the contract lives in `Kindred.SharedKernel/Messaging/Events/`. Save
  first, then publish. Construct the event INLINE (`new XIntegrationEvent(...)`, not `new(...)`)
  in the handler file that injects `IEventBus` — the knowledge graph reads PUBLISHES from that
  exact shape. Consumers implement `IIntegrationEventHandler<XIntegrationEvent>`, are
  idempotent, and are registered with `AddIntegrationEventHandler<TEvent, THandler>()`.
- Services talk over RabbitMQ, never HTTP to each other.
- EF: aggregates own their children (`OwnsMany`, backing field); keys the domain assigns are
  `ValueGeneratedNever()`.
```

---

## Frontend — `angular-frontend-architect`

Append:

```
### Frontend slice order
DTO model → api service method → facade → page (.ts/.html/.scss) → route + nav link

### Where things go (apps/frontend/src/app)
- core/models/api.models.ts     typed DTOs matching the API exactly
- core/api/<area>.api.ts        thin HttpClient wrappers returning Observable<Dto>; no state
- core/state/<area>.facade.ts   signals (data, loading, error) + methods; the only thing pages touch
- pages/<name>/<name>.page.*    one routed component per page
- app.routes.ts, layout/header/header.ts   one lazy route + one nav entry per page

### Hard constraints
- LOAD THE `new-module-recipe` SKILL before writing any page markup. The look comes from the
  shared primitives and page conventions it catalogs.
- Separate `.ts` / `.html` / `.scss` files. NEVER inline `template:` or `styles:`.
- Standalone components, OnPush, signals, `inject()`, `@if`/`@for` (never `*ngIf`/`*ngFor`).
- Pages talk to facades only — never to `HttpClient` or an api service directly.
- No NgRx, no UI library, no new npm dependencies.
- Buttons, cards, badges, fields, alerts, skeletons and empty states come from
  `src/styles/_primitives.scss`. Component SCSS is layout/spacing and page-specific blocks only.
- Tokens only (`var(--color-*)`, `var(--space-*)`, …) — no raw colours. Dark mode comes for free
  from the tokens; never write a dark-specific rule in a component.
- Every data view handles four states: error (with Retry), loading (skeleton), empty, loaded.
- Accessible: real buttons/links, labels, visible focus, `role="alert"` on errors.
- Backend not ready? Code against the DTO contract in `specs/**`, and flag it in your report
  for wave-gate integration.
```

---

## Tests — `test-architect`

Append:

```
### Constraints
- xUnit, in `tests/Kindred.<X>Service.UnitTests/` (reference the domain/application project only).
- Test the aggregate's rules: resulting state AND raised domain events, plus every rejection
  (error code + ErrorType). See `tests/Kindred.StudentService.UnitTests/StudentTests.cs`.
- Business-focused test names (behaviour, not method names): `A_student_cannot_take_more_than_six_courses`.
- Build objects through the domain's own factory methods (`Student.Register`, `CourseCode.From`) —
  never by reflection or by bypassing invariants.
- No infrastructure in unit tests: no database, no RabbitMQ, no HTTP.
- Do not run the tests yourself — the orchestrator runs them at the wave gate.
```
