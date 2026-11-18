using System.Text.Json;
using Kindred.KnowledgeGraph.Domain;

namespace Kindred.KnowledgeGraph.Infrastructure.Ingestion;

/// <summary>
/// Orchestrates one ingest/resync run: Tier A facts → Tier B conventions → gate → write,
/// then persists the validation report (knowledge-graph/last-validation-report.json).
/// </summary>
public sealed class IngestionPipeline(KgContext ctx)
{
    public async Task<ValidationReport> RunAsync(
        IReadOnlySet<string> tiers, IReadOnlyList<string>? scopeFiles = null)
    {
        var commit = ctx.Git.GetHeadCommit();
        var tierA = new TierAExtractor(ctx.RepoRoot, commit);
        var tierB = new TierBExtractor(ctx.RepoRoot, commit, ctx.Spine);

        var batch = new MutationBatch();

        // ── Tier A: deterministic facts (always computed — Layer 2 needs RepoFacts) ──
        var (svcNodes, svcEdges, _) = tierA.ExtractDotnetProjects();
        tierA.ExtractLaunchPorts(svcNodes);
        var facts = tierA.BuildRepoFacts(ctx.Git.ListTrackedFiles());

        if (tiers.Contains(Ontology.Tier.A))
        {
            svcNodes.ForEach(batch.Add);
            svcEdges.ForEach(batch.Add);

            var (eventNodes, eventEdges) = tierA.ExtractIntegrationEvents();
            eventNodes.ForEach(batch.Add);
            eventEdges.ForEach(batch.Add);

            var (taxNodes, taxEdges) = tierA.ExtractTaxonomies();
            taxNodes.ForEach(batch.Add);
            taxEdges.ForEach(batch.Add);
        }

        // ── Tier B: conventions + spine ──────────────────────────────────────
        var unbound = new Dictionary<string, List<string>>();
        var gatewayRun = false;
        if (tiers.Contains(Ontology.Tier.B))
        {
            var (spineNodes, spineEdges) = tierB.ExtractSpine(ctx.SpineFile);
            spineNodes.ForEach(batch.Add);
            spineEdges.ForEach(batch.Add);

            var (featNodes, featEdges) = tierB.ExtractFeaturesIndex();
            featNodes.ForEach(batch.Add);
            featEdges.ForEach(batch.Add);

            tierB.ExtractSpecRequirements().ForEach(batch.Add);

            var (deltaNodes, deltaEdges) = tierB.ExtractChangeDeltas();
            deltaNodes.ForEach(batch.Add);
            deltaEdges.ForEach(batch.Add);

            var (linkNodes, linkEdges) = tierB.ExtractMarkdownLinks(facts, scopeFiles);
            linkNodes.ForEach(batch.Add);
            linkEdges.ForEach(batch.Add);

            var serviceKeys = svcNodes.Select(n => n.Key).ToHashSet(StringComparer.Ordinal);
            tierB.ExtractGatewayRoutes(serviceKeys).ForEach(batch.Add);
            gatewayRun = true;

            var (boundEdges, unboundNames) = tierB.BindToSpine(batch.Nodes);
            boundEdges.ForEach(batch.Add);
            unbound = unboundNames;
        }

        // ── Gate + write ─────────────────────────────────────────────────────
        await ctx.Store.EnsureSchemaAsync();
        var existing = await ctx.Store.GetExistingNodeKeysAsync();
        var gated = new ValidationGate(facts, ctx.Spine).Run(batch, existing);
        var result = await ctx.Store.WriteAsync(gated);

        // The gateway extractor owns its CALLS edges: whatever this run did not write is retracted.
        var retracted = gatewayRun
            ? await ctx.Store.RetractEdgesNotReEmittedAsync(
                TierBExtractor.GatewayExtractorName,
                gated.Edges.Where(e => e.Provenance.Extractor == TierBExtractor.GatewayExtractorName)
                    .Select(e => e.EdgeId).ToList())
            : 0;

        await ctx.Store.SetLastIngestedCommitAsync(commit);

        var report = ValidationReport.From(result, unbound, commit) with { GatewayCallsRetracted = retracted };
        report.Save(ctx.RepoRoot);
        return report;
    }

    /// <summary>Resync: scope re-extraction to git-diff files; tombstone content from deleted files.</summary>
    public async Task<ValidationReport> ResyncAsync()
    {
        var last = await ctx.Store.GetLastIngestedCommitAsync()
            ?? throw new InvalidOperationException("No previous ingest recorded — run 'ingest run' first.");

        var head = ctx.Git.GetHeadCommit();
        if (last == head)
        {
            Console.WriteLine("Graph is already at HEAD — nothing to resync.");
            return ValidationReport.Empty(head);
        }

        var changed = ctx.Git.GetChangedFiles(last, head);
        Console.WriteLine($"Resync {last[..8]}..{head[..8]}: {changed.Count} changed files");

        // Tombstone content derived from files that no longer exist (e.g. a change folder moved
        // into openspec/changes/archive/ — the old paths go, the archive paths are re-ingested).
        var deleted = changed
            .Where(f => !File.Exists(Path.Combine(ctx.RepoRoot, f.Replace('/', Path.DirectorySeparatorChar))))
            .ToList();
        // Directory-sourced nodes (Change nodes are stamped with their folder) go with their folder.
        var deletedDirs = deleted
            .Select(f => Path.GetDirectoryName(f.Replace('/', Path.DirectorySeparatorChar))?.Replace('\\', '/'))
            .Where(d => !string.IsNullOrEmpty(d) &&
                        !Directory.Exists(Path.Combine(ctx.RepoRoot, d!.Replace('/', Path.DirectorySeparatorChar))))
            .Select(d => d!)
            .Distinct()
            .ToList();
        // Sources the graph still cites that are gone from the working tree. `ingest run` reads the
        // working tree, so a change can be ingested before it is ever committed; if it is then
        // archived before the next commit, git never saw its original path and the diff cannot
        // report it as deleted. Without this, its edges would outlive the folder they cite.
        var vanished = (await ctx.Store.GetSourceFilesAsync())
            .Where(p =>
            {
                var full = Path.Combine(ctx.RepoRoot, p.Replace('/', Path.DirectorySeparatorChar));
                return !File.Exists(full) && !Directory.Exists(full);
            })
            .ToList();
        var tombstoneSet = deleted.Concat(deletedDirs).Concat(vanished).Distinct().ToList();
        if (tombstoneSet.Count > 0)
        {
            var (edges, nodes) = await ctx.Store.TombstoneSourceFilesAsync(tombstoneSet);
            Console.WriteLine($"tombstoned {tombstoneSet.Count} deleted paths → removed {edges} edges, {nodes} nodes");
        }

        // Full re-run of the cheap deterministic tiers; markdown links scoped to the diff.
        return await RunAsync(new HashSet<string> { Ontology.Tier.A, Ontology.Tier.B },
            changed.Where(f => f.StartsWith("docs/") && f.EndsWith(".md")).ToList());
    }
}

/// <summary>Persistent per-run validation report (console + JSON).</summary>
public sealed record ValidationReport(
    string Commit,
    string BatchId,
    int NodesWritten,
    int EdgesWritten,
    int Disputed,
    int Proposed,
    IReadOnlyList<Rejection> Rejections,
    Dictionary<string, List<string>> Unbound,
    int GatewayCallsRetracted = 0)
{
    public static ValidationReport From(BatchResult result, Dictionary<string, List<string>> unbound, string commit) =>
        new(commit, result.BatchId, result.NodesWritten, result.EdgesWritten,
            result.DisputedCount, result.ProposedCount, result.Rejections.ToList(), unbound);

    public static ValidationReport Empty(string commit) => new(commit, "none", 0, 0, 0, 0, [], []);

    public void Save(string repoRoot)
    {
        var dir = Path.Combine(repoRoot, "knowledge-graph");
        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, "last-validation-report.json"),
            JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public void PrintSummary()
    {
        Console.WriteLine($"batch {BatchId} @ {Commit[..Math.Min(8, Commit.Length)]}");
        Console.WriteLine($"  nodes written : {NodesWritten}");
        Console.WriteLine($"  edges written : {EdgesWritten}");
        Console.WriteLine($"  DISPUTED      : {Disputed}");
        Console.WriteLine($"  PROPOSED      : {Proposed}");
        Console.WriteLine($"  rejected      : {Rejections.Count}");
        if (GatewayCallsRetracted > 0)
            Console.WriteLine($"  gateway CALLS : {GatewayCallsRetracted} stale edges retracted");
        foreach (var group in Rejections.GroupBy(r => r.Reason).OrderByDescending(g => g.Count()).Take(10))
            Console.WriteLine($"    {group.Key}: {group.Count()}");
        foreach (var (kind, names) in Unbound)
            Console.WriteLine($"  UNBOUND {kind}: {names.Count} ({string.Join(", ", names.Take(8))}{(names.Count > 8 ? ", …" : "")})");
        Console.WriteLine("  full report → knowledge-graph/last-validation-report.json");
    }
}

/// <summary>
/// Everything the gate needs for an out-of-pipeline write (Tier C import, duplicate-candidate
/// persistence): corroboration facts, the existing node-key universe, and the commit stamped.
/// </summary>
public sealed record GateInputs(string Commit, RepoFacts Facts, IReadOnlySet<string> ExistingKeys)
{
    public static async Task<GateInputs> LoadAsync(KgContext ctx, bool requireNonEmptyGraph = true)
    {
        var commit = ctx.Git.GetHeadCommit();
        var facts = new TierAExtractor(ctx.RepoRoot, commit).BuildRepoFacts(ctx.Git.ListTrackedFiles());
        var existing = await ctx.Store.GetExistingNodeKeysAsync();
        if (requireNonEmptyGraph && existing.Count == 0)
            throw new InvalidOperationException("Graph is empty — run 'ingest run' (Tiers A+B) first.");
        return new GateInputs(commit, facts, existing);
    }
}
