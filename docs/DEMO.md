# Demo runbook — "Your AI Has Amnesia. Let's Fix That."

The exact commands, in order. Rehearse on a throwaway branch (see "Reset between rehearsals").

**What has been verified, and what has not** (as of 2026-11-17):

- Verified: the app end to end (every endpoint through the gateway, the event flow, the three
  pages); steps 1 and 3 exactly as written; and the graph side of steps 4–6 with a stand-in
  change — UNBOUND before spine registration, the `IN_FLIGHT` spec and active change visible
  while uncommitted, `openspec archive`, the STALE advisory, `ingest resync`, the
  `IN_FLIGHT` → `CURRENT` flip, and the `kg` MCP server.
- **Not yet run:** the three `/kindred-*` skills themselves, driven by an agent in a fresh
  session, building the real feature (steps 4–6 as a whole) — that is the rehearsal. Also
  `scripts/start-dev.ps1` on its happy path: during setup another stack held the default
  ports, so only its port pre-flight was exercised and the services were started by hand.

## 0. Before the talk (once, off stage)

```powershell
# Kindred needs ports 5432, 5672, 15672, 7474, 7687, 5000, 5101, 5102, 4200.
# Stop anything else holding them (the start script refuses to run and names the culprit).

cd C:\Projects\KINDRED
dotnet build Kindred.slnx                 # also builds the exe the kg MCP server runs
npm install --prefix apps/frontend        # only if node_modules is missing

# Clean slate: empty databases, empty graph.
./scripts/stop-dev.ps1 -Wipe
./scripts/start-dev.ps1 -Kg               # postgres, rabbitmq, neo4j, services, frontend

# A short name for the knowledge graph tool, for this terminal:
Set-Alias kg (Resolve-Path tools/Kindred.KnowledgeGraph/bin/Debug/net10.0/Kindred.KnowledgeGraph.exe)
```

Open Claude Code in `C:\Projects\KINDRED` **after** the build, so the `kg` MCP server starts
(`/mcp` should list `kg` with `kg_query` and `kg_stats`). Do not rebuild
`tools/Kindred.KnowledgeGraph` while a session is open — the running server locks the exe.

Check: http://localhost:4200 shows twelve students; `git status` is clean; `git log` shows the
v1 commit.

## 1. Tour the app (2 min)

http://localhost:4200 → sign in as **Amira Haddad** → **My courses** (enroll in one, drop it
again; try a 7th course to show the rule) → **Classmates** (grouped by course, shared-count
badges).

Optional, to show the two services really are separate:
http://localhost:5000/api/v1/matching/courses/COMP-248/roster — matching-service's own copy,
built only from events. RabbitMQ UI: http://localhost:15672 (`kindred` / `kindred_dev`).

## 2. "The AI has amnesia"

A fresh Claude Code session in an empty folder (or `claude` with no project): ask
*"Does Kindred have study group matching? What would break if I changed enrollment?"*
It cannot know.

## 3. Give it a memory — build the graph

```powershell
kg ingest run        # deterministic facts + conventions, no LLM   → 0 UNBOUND, 0 rejected
kg ingest embed      # local embeddings, no API key                → full coverage per label
kg q stats           # what is in there
```

Neo4j browser, for the picture: http://localhost:7474 (`neo4j` / `kindred_kg_dev`)

```cypher
MATCH (n:KgNode)-[r]->(m:KgNode) RETURN n, r, m
```

Now ask it things:

```powershell
kg q query "does group matching exist?" --kind existence
```

→ `capability:study-group-matching` (declared in the spine: "course rosters today; forming
study groups … next") and `spec:course-rosters`. No group-matching spec, feature or change.
**Cited answer: not yet.**

```powershell
kg q query "what touches student enrollment?" --kind blast
```

→ the line to read out:

```
service:student-service -[PUBLISHES]-> event:StudentEnrolledInCourseIntegrationEvent   src=…/Commands/EnrollInCourse.cs@<commit>
event:StudentEnrolledInCourseIntegrationEvent -[CONSUMED_BY]-> service:matching-service src=…/Application/EnrollmentEventHandlers.cs@<commit>
module:Matching -[BRIDGES]-> module:Students via=student-enrolled-in-course
service:gateway -[CALLS]-> service:student-service routes=[/api/v1/students/{**rest}, /api/v1/courses/{**rest}]
```

Every fact names the file and commit it came from. Nobody wrote these edges by hand — they
are read from the code.

## 4. Propose the feature

In Claude Code:

```
/kindred-propose add-availability-and-group-matching

Students declare their weekly availability (days of the week × morning / afternoon / evening).
matching-service forms study groups: students who take the same course and share at least one
availability slot, in groups of up to four. The frontend gets an availability editor on
"My courses" and a new "My groups" page. Keep it as small as the rest of Kindred.
```

What the audience sees, in order:

1. **Existence gate** — the agent queries the graph *before creating anything* and reports,
   with citations, that group matching does not exist yet.
2. **OpenSpec change** — `openspec/changes/add-availability-and-group-matching/` with
   proposal, design, spec deltas and tasks (one section per service + frontend).
3. **Spine registration** — the change name is added to `study-group-matching` in
   `knowledge-graph/capabilities.yaml`, then `ingest run`.
4. **The graph already knows** — ask again:

```powershell
kg q query "does group matching exist?" --kind existence
```

→ `change:add-availability-and-group-matching … change=active tasks=0/N` and a spec with
`authority=IN_FLIGHT`. Work in progress is in the memory from the first minute — a second
agent asking the same question now would not start a duplicate.

## 5. Build it with parallel teams

```
/kindred-apply add-availability-and-group-matching --plan-teams-only
```

Shows the split **without spending anything**: the blast radius from the graph, the wave
plan, and the file-ownership matrix (one team per service + one for the frontend; shared
kernel and gateway kept by the orchestrator). Then for real:

```
/kindred-apply add-availability-and-group-matching
```

Wave 0 writes the integration event contract; then three agents work at the same time; then
the wave gate: `dotnet build`, `dotnet test`, the frontend build, and the migrations.

**Before the wave gate runs, close the three service windows** (keep Docker running) — a
running service locks its dlls and the build fails with file-in-use. Afterwards restart them:

```powershell
./scripts/start-dev.ps1 -Kg      # Docker is already up, so this just rebuilds and reopens the services
```

Then show the feature in the browser: set availability, open **My groups**.

> This step takes several minutes. Talk over it (the matrix, the "one writer per file" rule,
> why agents never commit), or cut to a rehearsal branch where it is already built.

## 6. Ship it — the graph remembers

```
/kindred-commit-n-archive add-availability-and-group-matching
```

Archive (delta specs become main specs) → **you approve the commit** → `ingest resync` +
`ingest embed`. Then the closing query — same question as step 3, different answer:

```powershell
kg q query "does group matching exist?" --kind existence
```

→ the spec is `authority=CURRENT`, the change is `change=archived`, and the new event shows
up in the blast radius of enrollment. Open a brand-new Claude Code session and ask it the
question from step 2. This time it knows.

## Reset between rehearsals

Rehearse on a branch so `main` stays at v1:

```powershell
git switch -c rehearsal            # before step 4
# … run steps 3–6 …
git switch main
git branch -D rehearsal
git clean -fd                      # removes anything the rehearsal left untracked
./scripts/stop-dev.ps1 -Wipe       # empty databases and an empty graph again
```

## If something goes wrong on stage

| Symptom | Fix |
|---|---|
| `start-dev.ps1` says ports are taken | It names the container/process. Stop it, run again. |
| Classmates / rosters are empty after a fresh start | student-service seeded before matching-service bound its queue. `./scripts/stop-dev.ps1 -Wipe`, start again. |
| `kg …` prints `[knowledge-graph] STALE` | `kg ingest resync`, then `kg ingest embed`. |
| `ingest run` lists something as UNBOUND | Add its name as an alias in `knowledge-graph/capabilities.yaml`, run again. (Worth showing once — it is the graph refusing to guess.) |
| The agent has no `kg_query` tool | The MCP exe was not built before the session started. The CLI (`kg q query …`) does the same thing. |
| Build fails with "file is being used by another process" | A service window or a `kg` MCP session is holding the dll. Close it, build again. |
| Neo4j is down | Every skill says so and carries on; run the graph steps afterwards. |
