using System.CommandLine;
using Kindred.KnowledgeGraph.Domain;
using Kindred.KnowledgeGraph.Infrastructure;
using Kindred.KnowledgeGraph.Infrastructure.Ingestion;

namespace Kindred.KnowledgeGraph.Cli;

internal static class IngestCommands
{
    public static Command Build()
    {
        var ingest = new Command("ingest", "Extract, validate, and write the graph (Tier A/B deterministic; Tier C via 'ingest import-edges').");

        var tiers = new Option<string>("--tiers") { Description = "Comma-separated extractor tiers to run (default: A,B)." };
        var full = new Command("run", "Full deterministic ingest (Tier A facts + Tier B conventions).");
        full.Add(tiers);
        full.SetAction(async (parseResult, ct) =>
        {
            await using var ctx = KgContext.Create();
            var tierSet = (parseResult.GetValue(tiers) ?? "A,B")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => t.ToUpperInvariant())
                .ToHashSet();
            if (tierSet.Contains(Ontology.Tier.C))
                Console.WriteLine("note: Tier C arrives separately via 'ingest import-edges --file <json>' (agent-extracted, gate-held).");

            var report = await new IngestionPipeline(ctx).RunAsync(tierSet);
            report.PrintSummary();
        });
        ingest.Add(full);

        var file = new Option<string>("--file")
        {
            Description = "JSON file of agent-extracted candidate edges (see TierCImport for the shape).",
            Required = true,
        };
        var importEdges = new Command("import-edges",
            "Tier C without a vendor: import agent-extracted candidate edges; every edge is pre-validated, provenance-stamped, and gate-held as PROPOSED.");
        importEdges.Add(file);
        importEdges.SetAction(async (parseResult, ct) =>
        {
            await using var ctx = KgContext.Create();
            var report = await new TierCImport(ctx).RunAsync(parseResult.GetValue(file)!);
            report.PrintSummary();
        });
        ingest.Add(importEdges);

        var resync = new Command("resync", "Incremental re-ingest scoped by git diff since the last ingested commit; tombstones deleted files.");
        resync.SetAction(async (_, ct) =>
        {
            await using var ctx = KgContext.Create();
            var report = await new IngestionPipeline(ctx).ResyncAsync();
            report.PrintSummary();
        });
        ingest.Add(resync);

        var embed = new Command("embed",
            "Compute/refresh embeddings for every seedable label (see SeedablePolicy), content-hash cached, and ensure the vector index.");
        embed.SetAction(async (_, ct) =>
        {
            await using var ctx = KgContext.Create();
            using var embeddings = new Infrastructure.Retrieval.EmbeddingService(ctx);
            var count = await embeddings.EmbedAllAsync();
            Console.WriteLine($"embedded/refreshed: {count} nodes (unchanged nodes skipped via content hash)");

            var coverage = await embeddings.GetCoverageAsync();
            Console.Write(Infrastructure.Retrieval.EmbeddingCoverage.Render(coverage));
            if (Infrastructure.Retrieval.EmbeddingCoverage.HasFailure(coverage))
            {
                Console.Error.WriteLine("FAIL: a seedable label has nodes but zero embedding coverage.");
                Environment.ExitCode = 1;
            }
        });
        ingest.Add(embed);

        return ingest;
    }
}
