using System.Text.Json;
using Kindred.KnowledgeGraph.Domain;

namespace Kindred.KnowledgeGraph.Infrastructure.Ingestion;

/// <summary>
/// Tier C without a vendor: ANY agent (a Claude Code session, a human, a future batch job)
/// extracts candidate edges from prose and hands them over as JSON; this class structurally
/// pre-validates, stamps Tier C provenance, and routes everything through the gate — so the
/// PROPOSED hold and the poisoning defenses are identical no matter who did the extraction.
///
/// Expected JSON shape (one file, any number of edges):
/// {
///   "extractor": "claude-code",                      // who extracted (goes into provenance)
///   "edges": [
///     { "type": "DEPENDS_ON", "fromKey": "feature:classmate-discovery", "toKey": "service:student-service",
///       "confidence": 0.9, "quote": "verbatim supporting sentence",
///       "sourceFile": "docs/features/classmate-discovery/README.md" }
///   ]
/// }
/// </summary>
public sealed class TierCImport(KgContext ctx)
{
    public async Task<ValidationReport> RunAsync(string jsonFile)
    {
        var path = Path.IsPathRooted(jsonFile)
            ? jsonFile
            : Path.Combine(ctx.RepoRoot, jsonFile.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path)) throw new FileNotFoundException(jsonFile);

        var payload = JsonSerializer.Deserialize<ImportPayload>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Import file is not valid JSON");
        if (payload.Edges is null || payload.Edges.Count == 0)
            throw new InvalidOperationException("Import file contains no edges");

        var extractor = $"tier-c-import/{payload.Extractor ?? "unknown"}";

        var (commit, facts, existing) = await GateInputs.LoadAsync(ctx);

        var batch = new MutationBatch();
        var preRejects = 0;

        foreach (var edge in payload.Edges)
        {
            // Structural pre-validation BEFORE gating: endpoints must already exist, and every
            // candidate needs a supporting quote + source file — no evidence, no entry.
            if (string.IsNullOrWhiteSpace(edge.Quote) || string.IsNullOrWhiteSpace(edge.SourceFile))
            {
                preRejects++;
                Console.WriteLine($"  pre-reject (missing quote/sourceFile): {edge.FromKey} -[{edge.Type}]-> {edge.ToKey}");
                continue;
            }
            if (!existing.Contains(edge.FromKey) || !existing.Contains(edge.ToKey))
            {
                preRejects++;
                Console.WriteLine($"  pre-reject (unknown endpoint): {edge.FromKey} -[{edge.Type}]-> {edge.ToKey}");
                continue;
            }

            batch.Add(new EdgeMutation(edge.Type, edge.FromKey, edge.ToKey,
                new Dictionary<string, object?> { ["confidence"] = edge.Confidence },
                new Provenance(
                    RepoPath.Normalize(ctx.RepoRoot, edge.SourceFile),
                    commit, Ontology.Tier.C, extractor,
                    edge.Quote.Length <= 300 ? edge.Quote : edge.Quote[..300])));
        }

        Console.WriteLine($"import: {batch.Edges.Count} candidate edges accepted for gating ({preRejects} pre-rejected)");

        await ctx.Store.EnsureSchemaAsync();
        var gated = new ValidationGate(facts, ctx.Spine).Run(batch, existing);
        var result = await ctx.Store.WriteAsync(gated);

        var report = ValidationReport.From(result, [], commit);
        report.Save(ctx.RepoRoot);
        return report;
    }

    private sealed record ImportPayload(string? Extractor, List<ImportEdge>? Edges);
    private sealed record ImportEdge(
        string Type, string FromKey, string ToKey, double Confidence, string? Quote, string? SourceFile);
}
