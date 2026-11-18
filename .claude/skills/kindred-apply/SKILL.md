---
name: kindred-apply
description: Implement an OpenSpec change with parallel agent teams — loads the change via the openspec CLI, grounds the work in the knowledge graph and the project rules, splits tasks.md into non-overlapping team slices with a file-ownership matrix, dispatches clean-code-architect / angular-frontend-architect / test-architect in waves, and gates each wave with a build. Use instead of openspec-apply-change for any change big enough to split (multiple tasks.md sections, or backend + frontend together).
---

# kindred-apply = openspec apply + parallel agent teams + KG grounding

The serial sibling is `openspec-apply-change` (one agent, one task at a time). Use **that**
for a handful of tasks in one file. Use **this** when the change has several tasks.md
sections that can run concurrently, or spans services + frontend.

The openspec **CLI commands** below are the stable contract — never paraphrase the sibling
skill's prose; if a flag looks wrong, read `openspec-apply-change` and trust it over this file.

---

## Arguments

`/kindred-apply [<change-name>] [flags]` — all optional. Parse them out of the invocation
before Step 1 and **echo the resolved settings** in the plan, so a mistyped flag is visible
rather than silently ignored.

| Flag | Default | Effect |
|---|---|---|
| `--plan-teams-only` | off | Run Steps 1–3, print the wave plan + ownership matrix, then **stop**. No agents spawned, nothing written. The cheap way to review a split before paying for it. |
| `--tasks <sel>` | all | Scope to a subset: `--tasks 3` (whole section), `--tasks 3.1,3.3` (individual), `--tasks 2-4` (section range). Out-of-scope tasks are excluded from every wave and reported as "not attempted" — never as blocked. |
| `--max-teams <n>` | 3 | Hard cap on concurrent teams per wave. Fewer teams than the cap is always allowed; more is not. |
| `--serial` | off | One agent at a time in dependency order, still with full grounding and wave gates. Equivalent to `--max-teams 1`. |
| `--no-kg` | auto | Skip Step 2b entirely (Neo4j known-down, or a self-contained new surface). Say in the plan that the blast radius is unverified, and widen the matrix accordingly. |

**Not flags, because they are invariants and not modes:** one writer per file, the wave gate,
and no commits. If any of those is in the way, the split is wrong — fix the split.

---

## Step 1 — Load the change

```bash
openspec list --json                              # if the change name is ambiguous, AskUserQuestion
openspec status  --change "<name>" --json         # schemaName + which artifact holds the tasks
openspec instructions apply --change "<name>" --json
```

Announce `Using change: <name>` and how to override.

Handle states: `blocked` → suggest `openspec-continue-change` and stop. `all_done` → suggest
`/kindred-commit-n-archive` and stop.

**Read every path under `contextFiles`** (spec-driven: proposal, design, specs/**, tasks).
Never assume filenames — take them from the CLI output.

---

## Step 2 — Ground the work

### 2a. Project rules (mandatory, by area touched)

Pull only what the change touches; hand the same list to each agent in its briefing.

| Change touches | Read first |
|---|---|
| Anything | `CLAUDE.md` and `openspec/config.yaml` (`context:` + `rules:` are hard constraints) |
| A .NET service | The existing slice that is closest in shape — it IS the cookbook. Command + events: `src/Services/StudentService/Kindred.StudentService/Application/Commands/EnrollInCourse.cs`. Aggregate: `.../Domain/Student.cs`. EF: `Kindred.StudentService.Data/Configurations.cs`. Endpoints: `Kindred.StudentService.Api/Endpoints/StudentEndpoints.cs`. Event consumer: `src/Services/MatchingService/Kindred.MatchingService/Application/EnrollmentEventHandlers.cs` |
| A new integration event | `src/SharedKernel/Kindred.SharedKernel/Messaging/` — contract in `Events/`, publisher constructs it inline in a file using `IEventBus`, consumer implements `IIntegrationEventHandler<T>` and is registered in the consuming service's `Program.cs` |
| A new gateway route | `src/Gateway/Kindred.Gateway/appsettings.json` — cluster id = target service's kebab name |
| Any new frontend page or list | **load the `new-module-recipe` skill** before writing markup, and read `apps/frontend/README.md` |
| Prior art | `docs/FEATURES.md` → `docs/features/<name>/` |

### 2b. Knowledge graph (as needed — not every change)

Query the graph when the change **modifies existing surfaces**, crosses services, or you are
unsure who depends on what. Skip it for a self-contained new surface, or when `--no-kg` is set.

```bash
dotnet run --project tools/Kindred.KnowledgeGraph -- q query "<topic>" --kind blast
```

(or MCP `kg_query`; load the `kg` skill for the full command table and the synthesis contract.)
Write the query in Spanish — the graph's text is Spanish (see "Language" in `CLAUDE.md`).

- Use the **blast radius** to build the file-ownership matrix — follow event flow end to end
  (publisher → event → every consumer), and both endpoints of a `BRIDGES` edge are in scope.
- Cite `sourceFile@commit`. `[DISPUTED]` is never presented as true; `[PROPOSED]` is an
  unconfirmed candidate only; a `spec:*` node with `authority: CURRENT` beats a doc.
- `[knowledge-graph] STALE:` → `ingest resync` + `ingest embed`, re-query. Splitting work off a
  stale graph is how two teams end up in the same file.
- Neo4j down → say so, offer `docker compose -f docker-compose.dev.yml --profile kg up -d neo4j`,
  and continue **without** it. Implementation is never blocked on the graph; just widen the
  ownership matrix, since you are guessing at the blast radius.

---

## Step 3 — Plan the split

`tasks.md` sections are the natural team boundary. Produce a **wave plan** before dispatching
anything.

1. **Wave 0 — blockers and contracts.** Tasks that answer a question rather than write code go
   to the **user** as an explicit question; never guess an answer and build on it. Then the
   **contract-first** step, done by the orchestrator itself: anything two teams must both
   compile against — a new integration event in `Kindred.SharedKernel/Messaging/Events/`, a
   new gateway route, the DTO shape the frontend will consume — is written (or pinned down in
   `specs/**`) BEFORE the teams start. That is what lets the publisher, the consumer and the
   frontend be built at the same time.
2. **Group into teams** — at most `--max-teams` (default 3), each owning a vertical slice and a
   disjoint file set. The natural split in this repo is one team per service plus one for the
   frontend. More teams means more collisions and a review surface no one can hold in their
   head, so under-filling the cap is a legitimate choice; exceeding it is not.
3. **Order within a slice is fixed** (`openspec/config.yaml` `rules.tasks`):
   - Backend: domain (aggregate + value objects + domain events) → EF Core configuration →
     command/query handler → repository → Minimal API endpoint + DTOs → xUnit tests
   - Frontend: DTO model → api service method → facade → page (.ts/.html/.scss) → route + nav link
   A frontend team starts against the DTO contract in `specs/**` before the endpoint exists,
   and integrates at the wave gate.
4. **File-ownership matrix.** Every file (or directory) an agent may write → exactly one team.
   Shared files — `src/SharedKernel/**`, `src/Gateway/**`, `knowledge-graph/capabilities.yaml`,
   `Kindred.slnx`, `Directory.Packages.props`, `docker-compose.dev.yml`, and on the frontend
   `src/styles/**` — are assigned to **one** team or kept by the orchestrator. Unassigned file =
   nobody writes it.
5. **`tasks.md` is orchestrator-owned.** No agent ever edits it. Agents report; you tick boxes.
6. **Migrations are orchestrator-owned.** Agents write the EF configuration; the orchestrator
   generates the migration at the wave gate (it needs a build, and agents do not build).
7. **Disjointness pre-flight — halting, always on.** Before dispatch, walk the matrix and prove
   no path appears in two rows. If you cannot make it disjoint: drop a team, move the contested
   file to orchestrator ownership, or push the contested task to a later wave. If none of those
   works, **stop and ask** — a wave that cannot be split safely is a wave that runs serially or
   not at all. Halting here is correct; two writers in one file is not a tradeoff you may make.

Print the wave plan, the resolved flags, and the matrix before dispatch. **`--plan-teams-only`
stops here.** If the build will span sessions, also write it to
`openspec/changes/<name>/orchestration.md` (scratch — openspec ignores it) so a later session
can resume without re-deriving the split.

---

## Step 4 — Dispatch a wave

All teams in a wave go out in **one message, multiple Agent calls**, so they run concurrently.
Under `--serial` (or `--max-teams 1`), dispatch one agent, gate it, then dispatch the next —
the grounding, ownership matrix, and wave gate all still apply.

| Work | Agent |
|---|---|
| .NET domain / handlers / repositories / endpoints | `clean-code-architect` |
| Angular pages, facades, api services | `angular-frontend-architect` |
| xUnit test suites for a slice that already landed | `test-architect` |
| Wide read-only reconnaissance before a slice | `Explore` |

Build each briefing from `references/agent-briefing.md`. Every briefing carries: the task IDs,
the acceptance criteria copied from `specs/**`, the files from Step 2a, the team's
**owned paths**, and the **shared-file ban list**.

### Non-negotiables in every briefing

- **Same working tree, current branch.** No `git worktree`, no branch switching, no `git stash`,
  no `git checkout -- <file>`, no `git reset`.
- **No commits, no pushes, no PRs.** Commits are the user's call.
- **Write only inside owned paths.** A needed edit outside them → **stop and report**, do not
  reach across. The orchestrator resolves it.
- **No builds, no migrations.** The orchestrator runs the wave gate. Concurrent `dotnet` builds
  collide on the shared kernel's `obj/` and `bin/`.
- **No destructive commands** — no `docker compose down -v`, no database drops, no row-deleting
  SQL, no `npm install`. Those need the user's approval, which an agent cannot obtain.
- **Do not edit `tasks.md`.**
- **Spanish for people, English for code.** UI text and API error messages are Mexican
  Spanish; identifiers, comments, error codes and paths stay English (`CLAUDE.md`, "Language").

### While a wave runs

If a team reports a collision, a design gap, or an ambiguous acceptance criterion: pause that
team, resolve it yourself, and — when the artifacts were wrong — update `design.md` / `specs/**`
rather than papering over it in code. That is the fluid-workflow contract, not a failure.

---

## Step 5 — Wave gate

After every wave, before the next dispatch and before ticking any box. Run **what the wave
touched**, never less:

```bash
dotnet build Kindred.slnx -nologo -v q
dotnet test tests/Kindred.StudentService.UnitTests -nologo -v q      # plus any new test project
npm run build --prefix apps/frontend                                  # when the frontend was touched
# a wave that changed an EF model: generate its migration now, one service at a time
dotnet ef migrations add <Name> -p src/Services/<X>Service/Kindred.<X>Service.Data -s src/Services/<X>Service/Kindred.<X>Service.Api
```

If the dev services are running, the build fails with file-in-use on their dlls — stop the
services first (close their windows; keep Docker up), gate, then restart them.

Then:
- Reconcile agent reports against the diff — `git status` / `git diff --stat`. A file changed
  outside its owner's matrix row is a collision: inspect it, don't assume it's fine.
- **Tick the completed boxes in `tasks.md`** (`- [ ]` → `- [x]`), appending a one-line note for
  anything that landed differently from the task text.
- Report progress `N/M`, then dispatch the next wave.

Red gate = no next wave. Fix it or surface it; never build on a broken tree.

---

## Step 6 — Close out

1. Confirm the change is registered in the capability spine — its name is in the matching
   capability's `aliases` in `knowledge-graph/capabilities.yaml`, and `ingest run` does **not**
   list it as UNBOUND. (`kindred-propose` normally did this already.)
2. Run the `openspec-verify-change` skill for the change.
3. Report:

```
## Implementation — <change-name>

**Progress:** N/M tasks complete
**Waves run:** <n>  |  **Teams:** <n>  |  **Flags:** <as resolved, or "defaults">
**Gate:** dotnet build ✓ · dotnet test ✓ · frontend build ✓

### Landed this session
- [x] 2.1 … (team 1, clean-code-architect)
- [x] 4.3 … (team 3, angular-frontend-architect)

### Open
- 0.1 — needs an answer from you: <question>
- 5.x — blocked on 0.1

### Not attempted (out of `--tasks` scope)
- 3.x — excluded by scope, not blocked

Next: /kindred-commit-n-archive  (commits are yours — nothing has been committed)
```

Then **stop**. No commit, no PR, no summary document — `/kindred-commit-n-archive` owns the
archive and the commit.

---

## Guardrails

- Selection, status, and instructions come from the **openspec CLI** — never from memory.
- Every wave ends green or ends the session.
- One writer per file, always. Ambiguity in the matrix is resolved before dispatch, not during.
- Pause on unclear tasks; artifacts get corrected, code does not get guessed.
- Nothing is committed. Ever. That is the user's call.
