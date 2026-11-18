using System.ComponentModel;
using Kindred.KnowledgeGraph.Infrastructure.Retrieval;
using Neo4j.Driver;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Kindred.KnowledgeGraph.Infrastructure;

/// <summary>
/// Stdio MCP server: exposes kg_query / kg_stats over the SAME retrieval engine as the CLI, so
/// citations and advisory behavior are identical in every surface. Registered in .mcp.json.
/// </summary>
public static class McpServerHost
{
    public static async Task RunAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        // Stdio transport: stdout belongs to the protocol; log to stderr only.
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<KgTools>();

        await builder.Build().RunAsync();
    }
}

[McpServerToolType]
public sealed class KgTools
{
    [McpServerTool(Name = "kg_query")]
    [Description("Structured retrieval from the Kindred knowledge graph (entirely LLM-free): vector seeds + typed graph traversal. Returns a ranked, provenance-cited subgraph for YOU to synthesize into an answer — cite the src= values, treat [DISPUTED]/[PROPOSED] as flagged, and remember both endpoints of a BRIDGES edge are in a blast radius. kind: 'existence', 'blast', or 'both' (default).")]
    public static async Task<string> Query(
        [Description("Natural-language question used for vector seeding")] string question,
        [Description("'existence' (does X exist / what is it), 'blast' (what touches X), or 'both'")] string kind = "both",
        [Description("Include unreviewed Tier-C PROPOSED elements (always labeled)")] bool includeProposed = false)
    {
        await using var ctx = KgContext.Create();
        using var embeddings = new EmbeddingService(ctx);
        var retriever = new HybridRetriever(ctx, embeddings);
        var sb = new System.Text.StringBuilder();
        if (await FreshnessProbe.CheckAsync(ctx) is string stale) sb.AppendLine(stale);
        sb.AppendLine(Cli.QueryCommands.AdvisoryBanner).AppendLine();
        if (!kind.StartsWith("blast", StringComparison.OrdinalIgnoreCase))
            sb.AppendLine((await retriever.RetrieveAsync(question, QuestionKind.Existence, includeProposed: includeProposed)).Render());
        if (!kind.StartsWith("exist", StringComparison.OrdinalIgnoreCase))
            sb.AppendLine((await retriever.RetrieveAsync(question, QuestionKind.BlastRadius, includeProposed: includeProposed)).Render());
        return sb.ToString();
    }

    [McpServerTool(Name = "kg_stats")]
    [Description("Node/edge counts by label/type of the Kindred knowledge graph.")]
    public static async Task<string> Stats()
    {
        await using var ctx = KgContext.Create();
        await using var session = ctx.Store.Driver.AsyncSession();
        var sb = new System.Text.StringBuilder();

        var labels = await session.RunAsync(
            "MATCH (n:KgNode) UNWIND [l IN labels(n) WHERE l <> 'KgNode'] AS label RETURN label, count(*) AS c ORDER BY c DESC");
        sb.AppendLine("nodes:");
        await labels.ForEachAsync(r => sb.AppendLine($"  {r["label"].As<string>()}: {r["c"].As<int>()}"));

        var edges = await session.RunAsync(
            "MATCH (:KgNode)-[r]->(:KgNode) RETURN type(r) AS t, count(*) AS c ORDER BY c DESC");
        sb.AppendLine("edges:");
        await edges.ForEachAsync(r => sb.AppendLine($"  {r["t"].As<string>()}: {r["c"].As<int>()}"));

        return sb.ToString();
    }
}
