---
name: kg
description: Query the Kindred knowledge graph (Neo4j "second brain") for existence and blast-radius questions — "does feature X already exist?", "what touches Y?", "is this a duplicate?". Use BEFORE building anything that might already exist, and when scoping a change that crosses services. Returns a raw provenance-cited subgraph that YOU synthesize into the answer — no LLM vendor anywhere in the pipeline.
---

# Kindred Knowledge Graph

Fully vendor-free: local ONNX embeddings + Neo4j traversal retrieve the evidence; **you write
the answer**. Prefer the MCP tool `kg_query` when the `kg` server is connected; otherwise:

```bash
dotnet run --project tools/Kindred.KnowledgeGraph -- q query "<question>"
```

**Ask in Spanish.** The graph's text (specs, docs, capability descriptions) is Mexican Spanish,
and the small embedding model matches a question to a description best when they share words —
an English question only catches the English identifiers left in the text, and scores the
capability that describes the thing much lower. Translate the user's question before you send
it, using the glossary in `CLAUDE.md` ("Language") so it shares words with the specs:
`"¿existe la formación de grupos de estudio?"`, `"¿qué toca la inscripción a cursos?"`.
Service and event names go as they are (`matching-service`). Answer the user in the language
they wrote in.

Prereq: `docker compose -f docker-compose.dev.yml --profile kg up -d neo4j`. No API keys needed
for anything in this skill.

**Freshness**: if query output starts with `[knowledge-graph] STALE: graph is N commits
behind HEAD`, run `ingest resync` then `ingest embed` and re-query before answering —
resync-then-answer, don't answer from a stale graph. The warning is advisory; results
still return if you knowingly accept staleness (say so in the answer).

**Lifecycle moments**: for exploring ideas, proposing changes, implementing them, or
archiving completed changes, use the wrapper skills `kindred-explore` / `kindred-propose` /
`kindred-apply` / `kindred-commit-n-archive` instead of raw queries — they run the right KG
steps (existence gate, spine registration, blast-radius split, post-commit archive resync) at
the right time.

## Which command for which question

| You want | Run | Notes |
|---|---|---|
| Answer any existence/impact question | `q query "<question>"` | Returns BOTH lenses (existence + blast-radius) by default; you synthesize |
| Only one lens (faster) | `q query "<q>" --kind existence` or `--kind blast` | |
| "Who publishes / consumes this event?" | `q query "¿qué toca <evento o tema>?" --kind blast` | `service -[PUBLISHES]-> event -[CONSUMED_BY]-> service`, read from code |
| "What does the gateway route where?" | `q query "¿qué rutas reenvía el gateway?" --kind blast` | `service:gateway -[CALLS {routes}]-> service:*`, read from the YARP config |
| "What is in flight?" | `q query "<topic>"` | Active changes are `Change` nodes with `tasksDone/tasksTotal`; a spec a change introduces is a `Spec` with `authority: IN_FLIGHT` |
| "Am I about to duplicate a feature?" | `q detect-duplicates` | Unanchored similarity + same-capability sibling overlap + cross-directory doc twins |
| Graph health / batches / provenance audit | `q stats [--audit-sample 25]` | |
| First ingest, or after editing the spine | `ingest run` then `ingest embed` | Full deterministic ingest (Tier A + B) |
| Refresh after commits | `ingest resync` then `ingest embed` | Scoped by git diff; deleted files tombstoned |
| Contribute prose-extracted edges (Tier C) | `ingest import-edges --file <json>` | YOU do the extraction (see below); the gate holds everything as PROPOSED |
| Review PROPOSED candidates | `review list` / `review approve --id <id>` / `review reject --id <id>` | |
| Undo a bad batch | `graph rollback --batch <id> [--dry-run]` | |

Every command is `dotnet run --project tools/Kindred.KnowledgeGraph -- <command>`.

## How to synthesize an answer from `q query` output (the quality contract)

Apply these every time:

1. **Claim only what an edge or node supports, and cite it** as `sourceFile@commit` from the
   `src=` values. If the subgraph lacks accepted evidence for something, say so plainly —
   never fill the gap.
2. **Statuses**: `[DISPUTED: reason]` contradicts repo facts — surface the dispute, never
   present it as true (it usually means the source doc is stale). `[PROPOSED]` is unreviewed
   Tier-C extraction — mention only as an unconfirmed candidate.
3. **Existence answers**: name the thing, its owning capability + module, its spec
   (`authority: CURRENT` is shipped truth and beats any doc; `authority: IN_FLIGHT` means a
   change is introducing it and it is NOT built yet — name the change), and its supersession
   status (`SUPERSEDED_BY` chains matter — don't recommend extending a deprecated surface).
4. **Blast-radius answers**: enumerate dependents INCLUDING everything reached across
   `BRIDGES` edges — both endpoints of a bridge are in the blast radius; note each bridge's
   `via`/`kind`/`bridgeStatus` (a `severed` bridge is history, not a live dependent). Follow
   event flow end to end: publisher → event → every consumer.
5. **Depth caveat**: the graph holds structure + pointers, not prose or source internals. For
   behavioral detail, Read the top cited file — and say that's what you're doing.
6. Keep the advisory framing: the graph is context, not a correctness guarantee.

## Contributing Tier C edges (extraction without a vendor)

When asked to enrich the graph from docs: Read the scoped files yourself, emit candidates to a
JSON file, then import. Only edges whose BOTH endpoints already exist in the graph are legal
(check `q stats` / prior queries); every edge needs a verbatim supporting quote:

```json
{ "extractor": "claude-code",
  "edges": [ { "type": "DEPENDS_ON", "fromKey": "feature:classmate-discovery",
               "toKey": "service:student-service", "confidence": 0.9,
               "quote": "Students, the catalog, enrollments, the classmates query",
               "sourceFile": "docs/features/classmate-discovery/README.md" } ] }
```

Allowed types: `OWNED_BY, DEPENDS_ON, SUPERSEDED_BY, DOCUMENTED_BY, INTRODUCED_BY, DECIDED_BY,
PUBLISHES, CONSUMED_BY, BOUND_TO`. Everything lands as `PROPOSED` until the user reviews —
never claim imported edges as accepted facts.

## Housekeeping

If a source shows UNBOUND in the `ingest run` summary (or
`knowledge-graph/last-validation-report.json`), extend `knowledge-graph/capabilities.yaml`
aliases (hand-edit) and re-run `ingest run` — the spine file is the only place capability ids
are minted.
