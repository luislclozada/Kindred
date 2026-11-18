# Knowledge Graph Schema Reference

> GENERATED from `Domain/Ontology.cs` by `graph schema` — do not edit by hand.
> The C# model is the source of truth; the structural validation layer rejects
> any write outside these closed sets.

## Node labels

- `Capability`
- `Change`
- `Decision`
- `Doc`
- `DomainEvent`
- `Feature`
- `Module`
- `Service`
- `Spec`

## Relationship types

- `BOUND_TO`
- `BRIDGES`
- `CALLS`
- `CONSUMED_BY`
- `DECIDED_BY`
- `DEPENDS_ON`
- `DOCUMENTED_BY`
- `DUPLICATES`
- `INTRODUCED_BY`
- `OWNED_BY`
- `PUBLISHES`
- `REFERENCES`
- `SPECIFIED_BY`
- `SUPERSEDED_BY`

## Required provenance properties (every node AND every relationship)

- `sourceFile`
- `commitHash`
- `extractorTier`
- `batchId`
- `extractedAt`
- `extractor`

## Statuses

- `ACCEPTED` — default; participates in query/ask answers
- `PROPOSED` — Tier C output awaiting review; excluded from answers by default
- `DISPUTED` — contradicted/uncorroborated by repo facts (`disputeReason` says why)

## Key conventions

- `capability:<id>` (spine-only mint), `module:<Name>`, `service:<kebab-name>`
  (Kindred.StudentService + .Api + .Data → `service:student-service`), `spec:<name>`,
  `change:<name>`, `feature:<name>`, `doc:<repo/relative/path.md>`, `event:<EventName>`,
  `decision:<change>/<id>`
- `Spec.authority`: `CURRENT` (openspec/specs) or `IN_FLIGHT` (a delta in an active change
  for a spec that does not exist yet) — `INTRODUCED_BY {delta: ADDED|MODIFIED}` links it to the change
- `CALLS` edges come only from the gateway's YARP config and are retracted when a route disappears