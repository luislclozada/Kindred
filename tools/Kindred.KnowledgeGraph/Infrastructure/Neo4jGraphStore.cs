using Kindred.KnowledgeGraph.Domain;
using Neo4j.Driver;

namespace Kindred.KnowledgeGraph.Infrastructure;

/// <summary>
/// The ONLY write path to the graph. Every mutation is stamped with its
/// batch id + provenance before it reaches Neo4j; unstamped writes throw at this layer.
/// Every node additionally carries the common label <c>KgNode</c> (unique key constraint,
/// cross-label MATCH) and a <c>batchIds</c> refcount array driving rollback survival.
/// </summary>
public sealed class Neo4jGraphStore(IDriver driver) : IAsyncDisposable
{
    public const string CommonLabel = "KgNode";

    public async Task EnsureSchemaAsync()
    {
        await using var session = driver.AsyncSession();
        await session.RunAndConsumeAsync(
            $"CREATE CONSTRAINT kg_node_key IF NOT EXISTS FOR (n:{CommonLabel}) REQUIRE n.key IS UNIQUE");
        await session.RunAndConsumeAsync(
            "CREATE INDEX kg_edge_batch IF NOT EXISTS FOR ()-[r:REFERENCES]-() ON (r.batchId)");
    }

    public async Task<IReadOnlySet<string>> GetExistingNodeKeysAsync()
    {
        await using var session = driver.AsyncSession();
        var cursor = await session.RunAsync($"MATCH (n:{CommonLabel}) RETURN n.key AS key");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        await cursor.ForEachAsync(r => keys.Add(r["key"].As<string>()));
        return keys;
    }

    // ── Write ────────────────────────────────────────────────────────────────

    public async Task<BatchResult> WriteAsync(GatedBatch gated)
    {
        foreach (var n in gated.Nodes) n.Provenance.Validate();
        foreach (var e in gated.Edges) e.Provenance.Validate();

        await using var session = driver.AsyncSession();
        var written = await session.ExecuteWriteAsync(async tx =>
        {
            var nodeCount = 0;
            foreach (var byLabel in gated.Nodes.GroupBy(n => n.Label))
            {
                var rows = byLabel.Select(n => new
                {
                    key = n.Key,
                    props = Stamp(n.Properties, n.Provenance, gated.BatchId),
                }).ToList();

                await tx.RunAndConsumeAsync(
                    $$"""
                    UNWIND $rows AS row
                    MERGE (n:{{CommonLabel}}:{{byLabel.Key}} { key: row.key })
                    SET n += row.props,
                        n.batchIds = CASE
                            WHEN n.batchIds IS NULL THEN [$batchId]
                            WHEN NOT $batchId IN n.batchIds THEN n.batchIds + $batchId
                            ELSE n.batchIds END
                    """,
                    new { rows, batchId = gated.BatchId });
                nodeCount += rows.Count;
            }

            var edgeCount = 0;
            foreach (var byType in gated.Edges.GroupBy(e => e.Type))
            {
                var rows = byType.Select(e => new
                {
                    from = e.FromKey,
                    to = e.ToKey,
                    props = Stamp(e.Properties, e.Provenance, gated.BatchId),
                }).ToList();

                await tx.RunAndConsumeAsync(
                    $$"""
                    UNWIND $rows AS row
                    MATCH (a:{{CommonLabel}} { key: row.from })
                    MATCH (b:{{CommonLabel}} { key: row.to })
                    MERGE (a)-[r:{{byType.Key}}]->(b)
                    SET r += row.props
                    """,
                    new { rows });
                edgeCount += rows.Count;
            }

            // Batch metadata record (writer-internal label, not part of the ontology).
            await tx.RunAndConsumeAsync(
                """
                MERGE (b:KgBatch { batchId: $batchId })
                SET b.writtenAt = datetime(),
                    b.nodesWritten = $nodes, b.edgesWritten = $edges,
                    b.disputed = $disputed, b.proposed = $proposed, b.rejected = $rejected,
                    b.sourceCommit = $commit
                """,
                new
                {
                    batchId = gated.BatchId,
                    nodes = nodeCount,
                    edges = edgeCount,
                    disputed = gated.DisputedCount,
                    proposed = gated.ProposedCount,
                    rejected = gated.Rejections.Count,
                    commit = gated.Nodes.FirstOrDefault()?.Provenance.CommitHash
                          ?? gated.Edges.FirstOrDefault()?.Provenance.CommitHash ?? "none",
                });

            return (nodeCount, edgeCount);
        });

        return new BatchResult(
            gated.BatchId, written.nodeCount, written.edgeCount,
            gated.DisputedCount, gated.ProposedCount, gated.Rejections);
    }

    private static Dictionary<string, object?> Stamp(
        IReadOnlyDictionary<string, object?> props, Provenance p, string batchId)
    {
        if (string.IsNullOrWhiteSpace(batchId))
            throw new InvalidOperationException("Unstamped write: batchId is required on every mutation");

        var stamped = new Dictionary<string, object?>(props)
        {
            ["sourceFile"] = p.SourceFile,
            ["commitHash"] = p.CommitHash,
            ["extractorTier"] = p.ExtractorTier,
            ["batchId"] = batchId,
            ["extractedAt"] = DateTime.UtcNow.ToString("O"),
            ["extractor"] = p.Extractor,
        };
        if (p.SupportingQuote is not null) stamped["supportingQuote"] = p.SupportingQuote;
        return stamped;
    }

    // ── Rollback (refcount survival, idempotent, dry-run) ─────────

    public async Task<RollbackReport> RollbackAsync(string batchId, bool dryRun)
    {
        await using var session = driver.AsyncSession();

        var edgeCount = await session.ExecuteReadAsync(async tx =>
        {
            var c = await tx.RunAsync(
                $"MATCH (:{CommonLabel})-[r]->(:{CommonLabel}) WHERE r.batchId = $batchId RETURN count(r) AS c",
                new { batchId });
            return (await c.SingleAsync())["c"].As<int>();
        });

        var nodeKeys = await session.ExecuteReadAsync(async tx =>
        {
            var c = await tx.RunAsync(
                $"""
                MATCH (n:{CommonLabel}) WHERE $batchId IN n.batchIds AND size(n.batchIds) = 1
                RETURN n.key AS key
                """,
                new { batchId });
            var keys = new List<string>();
            await c.ForEachAsync(r => keys.Add(r["key"].As<string>()));
            return keys;
        });

        if (!dryRun)
        {
            await session.ExecuteWriteAsync(async tx =>
            {
                await tx.RunAndConsumeAsync(
                    $"MATCH (:{CommonLabel})-[r]->(:{CommonLabel}) WHERE r.batchId = $batchId DELETE r",
                    new { batchId });
                await tx.RunAndConsumeAsync(
                    $"""
                    MATCH (n:{CommonLabel}) WHERE $batchId IN n.batchIds
                    SET n.batchIds = [x IN n.batchIds WHERE x <> $batchId]
                    WITH n WHERE size(n.batchIds) = 0
                    DETACH DELETE n
                    """,
                    new { batchId });
                await tx.RunAndConsumeAsync(
                    "MATCH (b:KgBatch { batchId: $batchId }) SET b.rolledBackAt = datetime()",
                    new { batchId });
                return true;
            });
        }

        return new RollbackReport(batchId, dryRun, edgeCount, nodeKeys);
    }

    // ── Tombstoning (resync: deleted files take their derived content with them) ──

    /// <summary>Every distinct source path stamped on a node or an edge.</summary>
    public async Task<IReadOnlyList<string>> GetSourceFilesAsync()
    {
        await using var session = driver.AsyncSession();
        var cursor = await session.RunAsync(
            $"""
            MATCH (n:{CommonLabel}) WHERE n.sourceFile IS NOT NULL RETURN DISTINCT n.sourceFile AS f
            UNION
            MATCH (:{CommonLabel})-[r]->(:{CommonLabel}) WHERE r.sourceFile IS NOT NULL RETURN DISTINCT r.sourceFile AS f
            """);
        return await cursor.ToListAsync(r => r["f"].As<string>());
    }

    /// <summary>
    /// Removes edges sourced from the deleted files, then nodes sourced from them that no
    /// other file's batch still claims (same refcount idea as rollback, keyed by sourceFile).
    /// Placeholder Doc nodes for the deleted paths are also removed so stale dead-link
    /// claims don't outlive their source.
    /// </summary>
    public async Task<(int Edges, int Nodes)> TombstoneSourceFilesAsync(IReadOnlyList<string> deletedRepoPaths)
    {
        if (deletedRepoPaths.Count == 0) return (0, 0);

        await using var session = driver.AsyncSession();
        return await session.ExecuteWriteAsync(async tx =>
        {
            var e = await tx.RunAsync(
                $"""
                MATCH (:{CommonLabel})-[r]->(:{CommonLabel}) WHERE r.sourceFile IN $paths
                WITH r, count(r) AS c DELETE r RETURN sum(c) AS c
                """,
                new { paths = deletedRepoPaths });
            var edges = (await e.SingleAsync())["c"].As<int>();

            var n = await tx.RunAsync(
                $"""
                MATCH (m:{CommonLabel})
                WHERE m.sourceFile IN $paths OR (m:Doc AND m.path IN $paths)
                WITH m, count(m) AS c DETACH DELETE m RETURN sum(c) AS c
                """,
                new { paths = deletedRepoPaths });
            var nodes = (await n.SingleAsync())["c"].As<int>();

            return (edges, nodes);
        });
    }

    // ── Retraction (an extractor that owns its edges outright: re-emit or lose them) ──

    /// <summary>
    /// Deletes every edge stamped by <paramref name="extractor"/> whose
    /// <see cref="EdgeMutation.EdgeId"/> is not in <paramref name="keepEdgeIds"/>. For extractors
    /// whose source is a complete inventory (the gateway route config): an edge the current run
    /// did not re-emit is no longer true, and MERGE alone would leave it in place forever. Nodes are
    /// never touched — they are claimed by other extractors too.
    /// </summary>
    public async Task<int> RetractEdgesNotReEmittedAsync(string extractor, IReadOnlyCollection<string> keepEdgeIds)
    {
        if (string.IsNullOrWhiteSpace(extractor))
            throw new ArgumentException("Retraction must be scoped to one extractor.", nameof(extractor));

        await using var session = driver.AsyncSession();
        return await session.ExecuteWriteAsync(async tx =>
        {
            var cursor = await tx.RunAsync(
                $"""
                MATCH (a:{CommonLabel})-[r]->(b:{CommonLabel})
                WHERE r.extractor = $extractor
                  AND NOT (a.key + '-[' + type(r) + ']->' + b.key) IN $keep
                WITH r, count(r) AS c DELETE r RETURN sum(c) AS c
                """,
                new { extractor, keep = keepEdgeIds.ToList() });
            return (await cursor.SingleAsync())["c"].As<int>();
        });
    }

    // ── Integrity audit (community-edition backstop for the provenance mandate) ──

    public async Task<int> CountUnstampedElementsAsync()
    {
        await using var session = driver.AsyncSession();
        return await session.ExecuteReadAsync(async tx =>
        {
            var n = await tx.RunAsync(
                $"MATCH (n:{CommonLabel}) WHERE n.batchId IS NULL RETURN count(n) AS c");
            var nodes = (await n.SingleAsync())["c"].As<int>();
            var e = await tx.RunAsync(
                $"MATCH (:{CommonLabel})-[r]->(:{CommonLabel}) WHERE r.batchId IS NULL RETURN count(r) AS c");
            var edges = (await e.SingleAsync())["c"].As<int>();
            return nodes + edges;
        });
    }

    // ── Ingestion metadata (resync cursor) ───────────────────────────────────

    public async Task<string?> GetLastIngestedCommitAsync()
    {
        await using var session = driver.AsyncSession();
        var cursor = await session.RunAsync("MATCH (m:KgMeta) RETURN m.lastIngestedCommit AS c LIMIT 1");
        var records = await cursor.ToListAsync();
        return records.Count == 0 ? null : records[0]["c"].As<string?>();
    }

    public async Task SetLastIngestedCommitAsync(string commit)
    {
        await using var session = driver.AsyncSession();
        await session.RunAndConsumeAsync(
            "MERGE (m:KgMeta) SET m.lastIngestedCommit = $commit, m.updatedAt = datetime()",
            new { commit });
    }

    public IDriver Driver => driver;

    public ValueTask DisposeAsync() => driver.DisposeAsync();
}

public sealed record RollbackReport(string BatchId, bool DryRun, int EdgesRemoved, IReadOnlyList<string> NodesRemoved);

internal static class Neo4jExtensions
{
    public static async Task RunAndConsumeAsync(this IAsyncSession session, string query, object? parameters = null)
    {
        var cursor = parameters is null ? await session.RunAsync(query) : await session.RunAsync(query, parameters);
        await cursor.ConsumeAsync();
    }

    public static async Task RunAndConsumeAsync(this IAsyncQueryRunner tx, string query, object? parameters = null)
    {
        var cursor = parameters is null ? await tx.RunAsync(query) : await tx.RunAsync(query, parameters);
        await cursor.ConsumeAsync();
    }
}
