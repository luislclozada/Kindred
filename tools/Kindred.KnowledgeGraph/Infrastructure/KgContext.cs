using Kindred.KnowledgeGraph.Domain;
using Neo4j.Driver;

namespace Kindred.KnowledgeGraph.Infrastructure;

/// <summary>
/// Wires the tool's collaborators from environment + repo conventions.
/// Env: NEO4J_URI (default bolt://localhost:11687), NEO4J_USER, NEO4J_PASSWORD.
/// </summary>
public sealed class KgContext : IAsyncDisposable
{
    public string RepoRoot { get; }
    public GitInfo Git { get; }
    public CapabilitySpine Spine { get; }
    public string SpineFile { get; }
    public Neo4jGraphStore Store { get; }

    private KgContext(string repoRoot, GitInfo git, CapabilitySpine spine, string spineFile, Neo4jGraphStore store)
    {
        RepoRoot = repoRoot;
        Git = git;
        Spine = spine;
        SpineFile = spineFile;
        Store = store;
    }

    public static KgContext Create()
    {
        var repoRoot = RepoRootLocator.Locate();
        var spineFile = Path.Combine(repoRoot, "knowledge-graph", "capabilities.yaml");
        if (!File.Exists(spineFile))
            throw new InvalidOperationException($"Capability spine not found: {spineFile}");

        var uri = Environment.GetEnvironmentVariable("NEO4J_URI") ?? "bolt://localhost:11687";
        var user = Environment.GetEnvironmentVariable("NEO4J_USER") ?? "neo4j";
        var password = Environment.GetEnvironmentVariable("NEO4J_PASSWORD") ?? "kindred_kg_dev";
        var driver = GraphDatabase.Driver(uri, AuthTokens.Basic(user, password));

        return new KgContext(
            repoRoot,
            new GitInfo(repoRoot),
            CapabilitySpine.Load(spineFile),
            spineFile,
            new Neo4jGraphStore(driver));
    }

    public ValueTask DisposeAsync() => Store.DisposeAsync();
}

/// <summary>
/// Read-time staleness advisory: compares the graph's last-ingested commit to repo HEAD.
/// Advisory only — retrieval always runs; the warning says how far behind and how to catch up.
/// </summary>
public static class FreshnessProbe
{
    public static async Task<string?> CheckAsync(KgContext ctx)
    {
        var last = await ctx.Store.GetLastIngestedCommitAsync();
        return Describe(last, ctx.Git.GetHeadCommit(), ctx.Git.TryCountCommitsBetween);
    }

    /// <summary>Pure composition so the fresh/behind/unrecorded branches are unit-testable.</summary>
    public static string? Describe(string? lastIngested, string head, Func<string, string, int?> countBehind)
    {
        if (string.IsNullOrEmpty(lastIngested))
            return "[knowledge-graph] STALE: no ingest recorded — run 'ingest run' then 'ingest embed'.";
        if (lastIngested == head)
            return null;

        var behind = countBehind(lastIngested, head);
        var distance = behind is int n
            ? $"{n} commit{(n == 1 ? "" : "s")}"
            : "an unknown number of commits";
        return $"[knowledge-graph] STALE: graph is {distance} behind HEAD " +
               $"({Short(lastIngested)} → {Short(head)}) — run 'ingest resync' then 'ingest embed'.";
    }

    private static string Short(string commit) => commit.Length <= 8 ? commit : commit[..8];
}
