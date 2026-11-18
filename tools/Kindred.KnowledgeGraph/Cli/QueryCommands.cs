using System.CommandLine;
using Kindred.KnowledgeGraph.Domain;
using Kindred.KnowledgeGraph.Infrastructure;
using Kindred.KnowledgeGraph.Infrastructure.Retrieval;
using Neo4j.Driver;

namespace Kindred.KnowledgeGraph.Cli;

internal static class QueryCommands
{
    public static Command Build()
    {
        var group = new Command("q", "Retrieval: query (LLM-free), detect-duplicates, stats.");
        group.Add(BuildQuery());
        group.Add(BuildDetectDuplicates());
        group.Add(BuildStats());
        return group;
    }

    public const string AdvisoryBanner =
        "[knowledge-graph] Advisory context from the Kindred graph — provenance-cited, but NOT a " +
        "correctness guarantee. Verify against specs/code before acting.";

    private static Command BuildQuery()
    {
        var question = new Argument<string>("question") { Description = "Natural-language question (used for vector seeding only — no LLM call)." };
        var kind = new Option<string>("--kind") { Description = "existence | blast | both (default both — the calling agent synthesizes the answer)." };
        var includeProposed = new Option<bool>("--include-proposed") { Description = "Include unreviewed Tier-C PROPOSED elements (labeled)." };
        var includeDisputed = new Option<bool>("--include-disputed") { Description = "Include DISPUTED elements (labeled with reason)." };

        var cmd = new Command("query", "Structured hybrid retrieval — vector seeds + typed traversal. Entirely LLM-free; the caller synthesizes.");
        cmd.Add(question);
        cmd.Add(kind);
        cmd.Add(includeProposed);
        cmd.Add(includeDisputed);
        cmd.SetAction(async (parseResult, ct) =>
        {
            await using var ctx = KgContext.Create();
            using var embeddings = new EmbeddingService(ctx);
            var retriever = new HybridRetriever(ctx, embeddings);
            var q = parseResult.GetValue(question)!;
            var proposed = parseResult.GetValue(includeProposed);
            var disputed = parseResult.GetValue(includeDisputed);
            var kindValue = parseResult.GetValue(kind) ?? "both";

            if (await FreshnessProbe.CheckAsync(ctx) is string stale) Console.WriteLine(stale);
            Console.WriteLine(AdvisoryBanner);
            Console.WriteLine();
            if (!kindValue.StartsWith("blast", StringComparison.OrdinalIgnoreCase))
            {
                var existence = await retriever.RetrieveAsync(q, QuestionKind.Existence,
                    includeProposed: proposed, includeDisputed: disputed);
                Console.WriteLine(existence.Render());
            }
            if (!kindValue.StartsWith("exist", StringComparison.OrdinalIgnoreCase))
            {
                var blast = await retriever.RetrieveAsync(q, QuestionKind.BlastRadius,
                    includeProposed: proposed, includeDisputed: disputed);
                Console.WriteLine(blast.Render());
            }
        });
        return cmd;
    }

    private static Command BuildDetectDuplicates()
    {
        var write = new Option<bool>("--write") { Description = "Write candidates as Tier-C PROPOSED DUPLICATES edges (never auto-accepted)." };

        var cmd = new Command("detect-duplicates", "Rank duplicate-feature candidates: similar text without a shared capability anchor, plus sibling overlap and cross-directory doc twins.");
        cmd.Add(write);
        cmd.SetAction(async (parseResult, ct) =>
        {
            await using var ctx = KgContext.Create();
            using var embeddings = new EmbeddingService(ctx);
            var retriever = new HybridRetriever(ctx, embeddings);
            var candidates = await retriever.DetectDuplicatesAsync();

            if (candidates.Count == 0) { Console.WriteLine("no duplicate candidates above thresholds"); return; }
            foreach (var c in candidates)
                Console.WriteLine($"  {c.Similarity:F3} [{c.Reason}] {c.A}  <->  {c.B}");

            if (parseResult.GetValue(write))
            {
                var (commit, facts, existing) = await Infrastructure.Ingestion.GateInputs.LoadAsync(ctx);
                var batch = new MutationBatch();
                foreach (var c in candidates)
                {
                    batch.Add(new EdgeMutation("DUPLICATES", c.A, c.B,
                        new Dictionary<string, object?> { ["similarity"] = c.Similarity, ["detector"] = c.Reason },
                        new Provenance("knowledge-graph/capabilities.yaml", commit, Ontology.Tier.C, "detect-duplicates/v1")));
                }

                var gated = new ValidationGate(facts, ctx.Spine).Run(batch, existing);
                var result = await ctx.Store.WriteAsync(gated);
                Console.WriteLine($"wrote {result.EdgesWritten} PROPOSED DUPLICATES edges (batch {result.BatchId})");
            }
        });
        return cmd;
    }

    private static Command BuildStats()
    {
        var auditSample = new Option<int>("--audit-sample") { Description = "Randomly audit N elements' provenance against git history." };

        var cmd = new Command("stats", "Graph statistics, batch history, and the random provenance audit.");
        cmd.Add(auditSample);
        cmd.SetAction(async (parseResult, ct) =>
        {
            await using var ctx = KgContext.Create();
            await using var session = ctx.Store.Driver.AsyncSession();

            var labelCursor = await session.RunAsync(
                "MATCH (n:KgNode) UNWIND [l IN labels(n) WHERE l <> 'KgNode'] AS label " +
                "RETURN label, count(*) AS c ORDER BY c DESC");
            Console.WriteLine("## Nodes by label");
            await labelCursor.ForEachAsync(r => Console.WriteLine($"  {r["label"].As<string>(),-12} {r["c"].As<int>()}"));

            var edgeCursor = await session.RunAsync(
                "MATCH (:KgNode)-[r]->(:KgNode) RETURN type(r) AS t, count(*) AS c, " +
                "sum(CASE WHEN r.status = 'DISPUTED' THEN 1 ELSE 0 END) AS disputed, " +
                "sum(CASE WHEN r.status = 'PROPOSED' THEN 1 ELSE 0 END) AS proposed ORDER BY c DESC");
            Console.WriteLine("## Edges by type (disputed/proposed)");
            await edgeCursor.ForEachAsync(r => Console.WriteLine(
                $"  {r["t"].As<string>(),-15} {r["c"].As<int>(),5}  ({r["disputed"].As<int>()}/{r["proposed"].As<int>()})"));

            var batchCursor = await session.RunAsync(
                "MATCH (b:KgBatch) RETURN b.batchId AS id, toString(b.writtenAt) AS at, " +
                "b.nodesWritten AS n, b.edgesWritten AS e, b.rolledBackAt IS NOT NULL AS rolledBack " +
                "ORDER BY b.writtenAt DESC LIMIT 10");
            Console.WriteLine("## Recent batches");
            await batchCursor.ForEachAsync(r => Console.WriteLine(
                $"  {r["id"].As<string>()}  {r["at"].As<string>()}  n={r["n"].As<int>()} e={r["e"].As<int>()}" +
                (r["rolledBack"].As<bool>() ? "  [ROLLED BACK]" : "")));

            var sample = parseResult.GetValue(auditSample);
            if (sample > 0) await RunProvenanceAudit(ctx, session, sample);
        });
        return cmd;
    }

    /// <summary>Random-sample audit: every sampled element's sourceFile must exist at its commitHash.</summary>
    private static async Task RunProvenanceAudit(KgContext ctx, IAsyncSession session, int sample)
    {
        var cursor = await session.RunAsync(
            """
            MATCH (n:KgNode) WITH n, rand() AS r ORDER BY r LIMIT $sample
            RETURN n.key AS key, n.sourceFile AS sourceFile, n.commitHash AS commit
            """,
            new { sample });
        var rows = await cursor.ToListAsync(r => (
            Key: r["key"].As<string>(),
            SourceFile: r["sourceFile"].As<string?>(),
            Commit: r["commit"].As<string?>()));

        var failures = 0;
        var uncommitted = 0;
        foreach (var (key, sourceFile, commit) in rows)
        {
            if (sourceFile is null || commit is null) { failures++; Console.WriteLine($"  AUDIT FAIL (unstamped): {key}"); continue; }
            // Directory-derived nodes (taxonomy folders) audit against the directory path.
            if (!ctx.Git.FileExistedAt(sourceFile, commit) && !ctx.Git.FileExistedAt($"{sourceFile}/", commit))
            {
                // Active-but-uncommitted work (e.g. an in-flight openspec change) is legitimate
                // second-brain content — it exists in the working tree, git just can't vouch yet.
                if (File.Exists(Path.Combine(ctx.RepoRoot, sourceFile.Replace('/', Path.DirectorySeparatorChar)))
                    || Directory.Exists(Path.Combine(ctx.RepoRoot, sourceFile.Replace('/', Path.DirectorySeparatorChar))))
                {
                    uncommitted++;
                    Console.WriteLine($"  audit note: {key} — {sourceFile} is uncommitted working-tree content");
                    continue;
                }

                failures++;
                Console.WriteLine($"  AUDIT FAIL: {key} claims {sourceFile}@{commit[..8]} but git has no such file");
            }
        }

        Console.WriteLine($"## Provenance audit: {rows.Count - failures}/{rows.Count} passed" +
                          (uncommitted > 0 ? $" ({uncommitted} uncommitted working-tree sources noted)" : ""));
        if (failures > 0) Environment.ExitCode = 1;
    }
}
