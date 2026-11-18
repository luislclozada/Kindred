# The Kindred knowledge graph

A provenance-stamped graph of this repo — its "second brain" — stored in Neo4j and built by
`tools/Kindred.KnowledgeGraph`. It joins four things that otherwise never meet (OpenSpec specs,
OpenSpec changes, docs, and the .NET source tree) through a hand-declared **capability spine**,
and answers two kinds of question with citations:

- **Existence** — "does group matching already exist?"
- **Blast radius** — "what touches student enrollment?"

> **Advisory context, not a correctness oracle.** Every fact carries `sourceFile@commit`.
> Specs and tests remain the enforcement layer.

## Run it

```powershell
# 1. Neo4j (opt-in compose profile)
docker compose -f docker-compose.dev.yml --profile kg up -d neo4j

# 2. Build the tool once (the MCP server runs the built exe)
dotnet build tools/Kindred.KnowledgeGraph

# 3. Ingest the repo — deterministic facts + convention parsing, no LLM
dotnet run --project tools/Kindred.KnowledgeGraph -- ingest run

# 4. Embeddings for retrieval — fully local, in-process ONNX model, no key, no cost
dotnet run --project tools/Kindred.KnowledgeGraph -- ingest embed

# 5. Ask
dotnet run --project tools/Kindred.KnowledgeGraph -- q query "does classmate discovery exist?"
dotnet run --project tools/Kindred.KnowledgeGraph -- q query "what touches enrollment?" --kind blast
dotnet run --project tools/Kindred.KnowledgeGraph -- q detect-duplicates
dotnet run --project tools/Kindred.KnowledgeGraph -- q stats
```

The tool reads the committed state of the repo (`git ls-files`, `git rev-parse HEAD`), so it
needs a git repository with at least one commit.

Config: `NEO4J_URI` (default `bolt://localhost:7687`), `NEO4J_USER` (`neo4j`), `NEO4J_PASSWORD`
(`kindred_kg_dev`) — from the environment or a `.env` file. No API keys of any kind.

## What is in the graph

| Node | Key | Comes from | Tier |
|---|---|---|---|
| `Service` | `service:student-service` | every `.csproj` under `src/` and `tools/` (`Kindred.StudentService{,.Api,.Data}` collapse into one) + port from `launchSettings.json` | A |
| `DomainEvent` | `event:StudentEnrolledInCourseIntegrationEvent` | a type implementing `IIntegrationEvent` | A |
| `Spec` | `spec:course-enrollment` | `openspec/specs/<name>/spec.md` (`authority: CURRENT`), or a delta in an active change for a spec that does not exist yet (`authority: IN_FLIGHT`) | A / B |
| `Change` | `change:<name>` | `openspec/changes/<name>/` and `archive/<date>-<name>/`, with `tasksDone/tasksTotal` | A |
| `Doc`, `Feature` | `doc:docs/FEATURES.md`, `feature:classmate-discovery` | `docs/**/*.md`, `docs/features/<name>/`, the `FEATURES.md` entry grammar | A / B |
| `Capability`, `Module` | `capability:study-group-matching`, `module:Matching` | **only** `knowledge-graph/capabilities.yaml` | B |

| Edge | Means | Comes from |
|---|---|---|
| `DEPENDS_ON` | service → service it references | `<ProjectReference>` |
| `PUBLISHES` | service → event | `new XIntegrationEvent(` in a file that uses `IEventBus` |
| `CONSUMED_BY` | event → service | a class implementing `IIntegrationEventHandler<XIntegrationEvent>` |
| `CALLS` | gateway → service, with the route paths | `ReverseProxy.Routes` in the gateway's `appsettings.json` (cluster id = service name) |
| `BOUND_TO` | spec / change / feature / service → capability | the spine's ids and aliases |
| `OWNED_BY` | capability → module | the spine |
| `BRIDGES` | module ↔ module seam | `bridges:` in the spine |
| `INTRODUCED_BY` | spec → change (`delta: ADDED \| MODIFIED`) | `openspec/changes/<c>/specs/<s>/spec.md` |
| `DOCUMENTED_BY`, `REFERENCES`, `SUPERSEDED_BY` | docs structure | `docs/` folders, markdown links, `FEATURES.md` |

Because publishers and consumers are read from code shape, keep the convention: construct the
integration event inline in the handler file that uses `IEventBus`, and implement
`IIntegrationEventHandler<T>` on the consuming side.

## How it stays trustworthy

- **Single write path** — every node and edge is stamped `{sourceFile, commitHash,
  extractorTier, batchId, extractedAt, extractor}`. Unstamped writes throw; `graph validate`
  audits the live graph.
- **Trust tier = risk route** — Tier A (repo facts) and Tier B (convention grammars) write
  directly. Tier C (edges an agent extracted from prose, `ingest import-edges --file <json>`)
  is held as `PROPOSED` until a human runs `review approve`.
- **Consistency layer** — a doc link to a missing file is written as `DISPUTED:
  target-missing`; a doc-claimed dependency with no corroborating repo fact is `DISPUTED:
  uncorroborated-by-repo-facts`. A wrong doc cannot silently become truth.
- **The capability spine** (`capabilities.yaml`) is the only place capability ids are minted.
  Sources that bind to nothing are reported as UNBOUND on every run — extend aliases by hand,
  re-ingest.
- **Reversible batches** — `graph rollback --batch <id> [--dry-run]`. `ingest resync` is scoped
  by `git diff` since the last ingested commit and tombstones deleted files.
- **Zero LLM vendors** — embeddings are an in-process ONNX model
  (`SmartComponents.LocalEmbeddings`, 384-dim). The tool retrieves and cites; the *calling
  agent* writes the answer, following the contract in the `kg` skill.

`graph schema` regenerates `knowledge-graph/schema-reference.md` from the C# ontology.

## The lifecycle — how the graph grows

The graph is kept true by the change lifecycle, not by a background job:

| Moment | Skill | What happens to the graph |
|---|---|---|
| Explore an idea | `kindred-explore` | Queried for cited current state before the thinking starts |
| Propose a change | `kindred-propose` | **Existence gate** before anything is created; then the change is registered in the spine and `ingest run` binds it — in-flight work is visible to existence queries from day one (a brand-new spec appears as `IN_FLIGHT`) |
| Implement | `kindred-apply` | The **blast radius** decides how the work is split between parallel teams |
| Archive + commit | `kindred-commit-n-archive` | After the commit, `ingest resync` + `ingest embed` record the new truth: the change is archived, its specs are `CURRENT` |

Safety net: every query compares the graph's last ingested commit with `HEAD` and prints
`[knowledge-graph] STALE: graph is N commits behind HEAD` when they differ. The remedy is
`ingest resync` then `ingest embed`.

Backstop: `openspec/config.yaml` requires proposals to cite the existence check and tasks to
include spine registration, even when the raw `openspec-*` skills are used.

## Claude Code wiring

- **Skill** `.claude/skills/kg/` — the command table and the answer-synthesis contract.
- **MCP** — registered as `kg` in `.mcp.json` (`graph mcp`, stdio). Tools: `kg_query`,
  `kg_stats`. Same engine and citations as the CLI.

  **`.mcp.json` launches the built exe, not `dotnet run` — do not change that.** `dotnet run`
  rebuilds on every launch, and the build fails whenever a running server already holds the
  dll. Consequences: build the tool once before first use, and stop every running `kg` server
  (close the Claude Code sessions using it) before rebuilding the tool.

## Known limits

- The graph indexes specs, changes, docs and project-level code facts — not source-code internals.
- Neo4j community has no property-existence constraints; the write path and `graph validate`
  enforce provenance instead.
- Human review of Tier C is a CLI (`review list | approve | reject`), not a UI.
