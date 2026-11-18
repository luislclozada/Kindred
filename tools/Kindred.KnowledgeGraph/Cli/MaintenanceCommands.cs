using System.CommandLine;
using Kindred.KnowledgeGraph.Domain;
using Kindred.KnowledgeGraph.Infrastructure;

namespace Kindred.KnowledgeGraph.Cli;

internal static class MaintenanceCommands
{
    public static Command Build()
    {
        var maint = new Command("graph", "Graph maintenance: rollback, integrity validation, schema reference, MCP server.");

        // ── rollback ─────────────────────────────────────────────────────────
        var batchId = new Option<string>("--batch") { Description = "Batch id to roll back.", Required = true };
        var dryRun = new Option<bool>("--dry-run") { Description = "Print what would be removed; write nothing." };
        var rollback = new Command("rollback", "Reversible-batch rollback: removes the batch's edges and refcount-zero nodes.");
        rollback.Add(batchId);
        rollback.Add(dryRun);
        rollback.SetAction(async (parseResult, ct) =>
        {
            await using var ctx = KgContext.Create();
            var report = await ctx.Store.RollbackAsync(parseResult.GetValue(batchId)!, parseResult.GetValue(dryRun));
            Console.WriteLine($"{(report.DryRun ? "[dry-run] would remove" : "removed")}: " +
                              $"{report.EdgesRemoved} edges, {report.NodesRemoved.Count} nodes");
            foreach (var key in report.NodesRemoved.Take(25)) Console.WriteLine($"  node {key}");
            if (report.NodesRemoved.Count > 25) Console.WriteLine($"  … +{report.NodesRemoved.Count - 25} more");
        });
        maint.Add(rollback);

        // ── validate --graph (integrity audit: Neo4j community has no property-existence constraints) ──
        var validate = new Command("validate", "Graph integrity audit: fails loudly on any element missing its batch stamp.");
        validate.SetAction(async (_, ct) =>
        {
            await using var ctx = KgContext.Create();
            var unstamped = await ctx.Store.CountUnstampedElementsAsync();
            if (unstamped > 0)
            {
                Console.Error.WriteLine($"INTEGRITY FAILURE: {unstamped} unstamped elements in the graph");
                Environment.ExitCode = 1;
            }
            else
            {
                Console.WriteLine("integrity OK: every node and edge carries its batch stamp");
            }
        });
        maint.Add(validate);

        // ── schema (generated ontology reference, versioned in source) ──
        var schema = new Command("schema", "Write knowledge-graph/schema-reference.md generated from the C# ontology model.");
        schema.SetAction((_, ct) =>
        {
            var repoRoot = Infrastructure.RepoRootLocator.Locate();
            var path = Path.Combine(repoRoot, "knowledge-graph", "schema-reference.md");
            File.WriteAllText(path, SchemaReference.Generate());
            Console.WriteLine($"wrote {path}");
            return Task.CompletedTask;
        });
        maint.Add(schema);

        // ── mcp (stdio server) ──────────────────────────────────
        var mcp = new Command("mcp", "Host the stdio MCP server exposing kg_query / kg_stats (register in .mcp.json).");
        mcp.SetAction(async (_, ct) => await Infrastructure.McpServerHost.RunAsync());
        maint.Add(mcp);

        return maint;
    }
}

internal static class SchemaReference
{
    public static string Generate() =>
        $"""
        # Knowledge Graph Schema Reference

        > GENERATED from `Domain/Ontology.cs` by `graph schema` — do not edit by hand.
        > The C# model is the source of truth; the structural validation layer rejects
        > any write outside these closed sets.

        ## Node labels

        {string.Join("\n", Ontology.NodeLabels.Order().Select(l => $"- `{l}`"))}

        ## Relationship types

        {string.Join("\n", Ontology.EdgeTypes.Order().Select(t => $"- `{t}`"))}

        ## Required provenance properties (every node AND every relationship)

        {string.Join("\n", Ontology.RequiredProvenanceProps.Select(p => $"- `{p}`"))}

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
          for a spec that does not exist yet) — `INTRODUCED_BY {"{delta: ADDED|MODIFIED}"}` links it to the change
        - `CALLS` edges come only from the gateway's YARP config and are retracted when a route disappears
        """;
}
