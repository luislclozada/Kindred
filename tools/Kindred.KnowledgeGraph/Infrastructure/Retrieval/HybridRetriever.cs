using System.Text;
using System.Text.RegularExpressions;
using Kindred.KnowledgeGraph.Domain;
using Neo4j.Driver;

namespace Kindred.KnowledgeGraph.Infrastructure.Retrieval;

public enum QuestionKind { Existence, BlastRadius }

/// <summary>
/// Hybrid GraphRAG retrieval: embed query → vector-index seeds (+ lexical service seeds) →
/// question-kind-typed 1–3 hop traversal → score fusion (vector × hop decay) → ranked subgraph.
/// DISPUTED/PROPOSED are excluded by default and labeled when included.
/// </summary>
public sealed class HybridRetriever(KgContext ctx, EmbeddingService embeddings)
{
    // Edge-type sets per question kind. Values come from the closed ontology, so interpolating
    // them into the Cypher pattern is safe.
    private static readonly string[] ExistenceTypes =
        ["BOUND_TO", "SPECIFIED_BY", "DOCUMENTED_BY", "INTRODUCED_BY", "SUPERSEDED_BY", "OWNED_BY"];
    private static readonly string[] BlastRadiusTypes =
        ["DEPENDS_ON", "BRIDGES", "PUBLISHES", "CONSUMED_BY", "BOUND_TO", "OWNED_BY", "SPECIFIED_BY",
         "INTRODUCED_BY", "DUPLICATES", "CALLS"];

    public async Task<RetrievalResult> RetrieveAsync(
        string question, QuestionKind kind, int seedCount = 8,
        bool includeProposed = false, bool includeDisputed = false)
    {
        foreach (var t in ExistenceTypes.Concat(BlastRadiusTypes))
            if (!Ontology.EdgeTypes.Contains(t))
                throw new InvalidOperationException($"Edge-type set drifted from ontology: {t}");

        var queryEmbedding = await embeddings.EmbedQueryAsync(question);
        var types = kind == QuestionKind.Existence ? ExistenceTypes : BlastRadiusTypes;
        var typePattern = string.Join('|', types);
        // Blast radius needs one more hop: seed (Spec) → capability → module → BRIDGES → other module.
        var maxHops = kind == QuestionKind.BlastRadius ? 3 : 2;

        await using var session = ctx.Store.Driver.AsyncSession();

        // 1) Vector seeds.
        var seedCursor = await session.RunAsync(
            $"""
            CALL db.index.vector.queryNodes('{EmbeddingService.IndexName}', $k, $embedding)
            YIELD node, score
            WHERE coalesce(node.status, 'ACCEPTED') = 'ACCEPTED' AND coalesce(node.missing, false) = false
            RETURN node.key AS key, score
            """,
            new { k = seedCount, embedding = queryEmbedding });
        var seeds = await seedCursor.ToListAsync(r => (Key: r["key"].As<string>(), Score: r["score"].As<double>()));

        // 1b) Lexical service seeds: Service nodes carry no embeddings, so a question naming one
        // ("what touches student-service?") is matched by name, as a whole word. The names come
        // from the graph itself — no hand-kept list to drift.
        var serviceCursor = await session.RunAsync(
            "MATCH (n:KgNode:Service) WHERE coalesce(n.status, 'ACCEPTED') = 'ACCEPTED' RETURN n.key AS key, n.name AS name");
        var services = await serviceCursor.ToListAsync(r => (Key: r["key"].As<string>(), Name: r["name"].As<string>()));
        var seeded = seeds.Select(s => s.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var (key, name) in services)
        {
            var spaced = name.Replace('-', ' ');
            if (Regex.IsMatch(question, $@"\b({Regex.Escape(name)}|{Regex.Escape(spaced)})\b", RegexOptions.IgnoreCase)
                && seeded.Add(key))
                seeds.Add((key, 1.0));
        }

        if (seeds.Count == 0) return RetrievalResult.Empty(question, kind);

        var statusFilter = BuildStatusFilter(includeProposed, includeDisputed);

        // 2) Typed traversal, status-filtered per flags.
        var pathCursor = await session.RunAsync(
            $$"""
            UNWIND $seeds AS seed
            MATCH (s:KgNode { key: seed.key })
            MATCH path = (s)-[:{{typePattern}}*1..{{maxHops}}]-(m:KgNode)
            WHERE ALL(rel IN relationships(path) WHERE {{statusFilter}})
              AND coalesce(m.missing, false) = false
            WITH seed, m, path, length(path) AS hops
            ORDER BY hops ASC
            WITH seed, m, collect(path)[0] AS path, min(hops) AS hops
            RETURN seed.key AS seedKey, seed.score AS seedScore, m.key AS key,
                   labels(m) AS labels, hops,
                   [rel IN relationships(path) | {
                       type: type(rel),
                       fromKey: startNode(rel).key, toKey: endNode(rel).key,
                       status: coalesce(rel.status, 'ACCEPTED'),
                       disputeReason: rel.disputeReason,
                       sourceFile: rel.sourceFile, commitHash: rel.commitHash,
                       via: rel.via, kind: rel.kind, bridgeStatus: rel.bridgeStatus,
                       delta: rel.delta, routes: rel.routes
                   }] AS rels
            LIMIT 400
            """,
            new { seeds = seeds.Select(s => new { key = s.Key, score = s.Score }).ToList() });

        var nodes = new Dictionary<string, RetrievedNode>();
        var edges = new Dictionary<string, RetrievedEdge>();

        foreach (var (key, score) in seeds)
            nodes[key] = new RetrievedNode(key, [], score);

        await pathCursor.ForEachAsync(r =>
        {
            var score = r["seedScore"].As<double>() * Math.Pow(0.6, r["hops"].As<int>());
            var key = r["key"].As<string>();
            if (!nodes.TryGetValue(key, out var existing) || existing.Score < score)
                nodes[key] = new RetrievedNode(key, r["labels"].As<List<string>>(), score);

            foreach (var rel in r["rels"].As<List<IDictionary<string, object>>>())
            {
                var edge = RetrievedEdge.From(rel);
                edges.TryAdd($"{edge.FromKey}|{edge.Type}|{edge.ToKey}", edge);
            }
        });

        // 3) Hydrate node detail for the ranked top slice.
        var top = nodes.Values.OrderByDescending(n => n.Score).Take(40).ToList();
        var detailCursor = await session.RunAsync(
            """
            UNWIND $keys AS key
            MATCH (n:KgNode { key: key })
            RETURN n.key AS key, labels(n) AS labels, n.name AS name, n.title AS title,
                   n.path AS path, n.status AS status, n.authority AS authority,
                   n.changeStatus AS changeStatus, n.tasksDone AS tasksDone, n.tasksTotal AS tasksTotal,
                   n.port AS port, n.sourceFile AS sourceFile, n.commitHash AS commitHash
            """,
            new { keys = top.Select(n => n.Key).ToList() });

        var detail = new Dictionary<string, IRecord>();
        await detailCursor.ForEachAsync(r => detail[r["key"].As<string>()] = r);

        // 4) Close the subgraph. The traversal keeps ONE shortest path per (seed, node), so when a
        // node is reachable several equally short ways the other ways' edges are dropped — e.g. a
        // consumer reached through a shared ProjectReference hides the event it consumes. Every
        // accepted relationship between two retrieved nodes is evidence the caller should see.
        var closureCursor = await session.RunAsync(
            $$"""
            MATCH (a:KgNode)-[rel:{{typePattern}}]->(b:KgNode)
            WHERE a.key IN $keys AND b.key IN $keys AND {{statusFilter}}
            RETURN {
                type: type(rel),
                fromKey: a.key, toKey: b.key,
                status: coalesce(rel.status, 'ACCEPTED'),
                disputeReason: rel.disputeReason,
                sourceFile: rel.sourceFile, commitHash: rel.commitHash,
                via: rel.via, kind: rel.kind, bridgeStatus: rel.bridgeStatus,
                delta: rel.delta, routes: rel.routes
            } AS rel
            """,
            new { keys = top.Select(n => n.Key).ToList() });
        await closureCursor.ForEachAsync(r =>
        {
            var edge = RetrievedEdge.From(r["rel"].As<IDictionary<string, object>>());
            edges.TryAdd($"{edge.FromKey}|{edge.Type}|{edge.ToKey}", edge);
        });

        var hydrated = top.Select(n =>
        {
            if (!detail.TryGetValue(n.Key, out var d)) return n;
            var done = d["tasksDone"].As<int?>();
            var total = d["tasksTotal"].As<int?>();
            return n with
            {
                Labels = d["labels"].As<List<string>>().Where(l => l != Neo4jGraphStore.CommonLabel).ToList(),
                Name = d["name"].As<string?>() ?? d["title"].As<string?>(),
                Path = d["path"].As<string?>(),
                Status = d["status"].As<string?>(),
                Authority = d["authority"].As<string?>(),
                ChangeStatus = d["changeStatus"].As<string?>(),
                Progress = total is > 0 ? $"{done}/{total}" : null,
                Port = d["port"].As<int?>(),
                SourceFile = d["sourceFile"].As<string?>(),
                CommitHash = d["commitHash"].As<string?>(),
            };
        }).ToList();

        return new RetrievalResult(question, kind, hydrated, edges.Values.ToList());
    }

    private static string BuildStatusFilter(bool includeProposed, bool includeDisputed)
    {
        var allowed = new List<string> { "'ACCEPTED'" };
        if (includeProposed) allowed.Add("'PROPOSED'");
        if (includeDisputed) allowed.Add("'DISPUTED'");
        return $"coalesce(rel.status, 'ACCEPTED') IN [{string.Join(", ", allowed)}]";
    }

    // ── Duplicate detection ──────────────────────────────────────────────────

    /// <summary>
    /// Three candidate classes: (1) unanchored similarity — Feature/Spec/Change pairs with no shared
    /// capability; (2) sibling overlap — pairs INSIDE one capability whose text is near-identical
    /// (same area, nobody connected them); (3) cross-directory Doc twins.
    /// Thresholds are on Neo4j's scale: vector.similarity.cosine returns (1 + cosine) / 2, so 0.95
    /// is a raw cosine of 0.90. Unrelated specs written in the same style reach 0.93 on this scale;
    /// two texts about the same thing score 0.96 and up.
    /// </summary>
    public async Task<IReadOnlyList<DuplicateCandidate>> DetectDuplicatesAsync(
        double anchoredThreshold = 0.95, double siblingThreshold = 0.95, double docThreshold = 0.95)
    {
        await using var session = ctx.Store.Driver.AsyncSession();

        var unanchored = await session.RunAsync(
            """
            MATCH (a:KgNode), (b:KgNode)
            WHERE (a:Feature OR a:Spec OR a:Change) AND (b:Feature OR b:Spec OR b:Change)
              AND a.key < b.key AND a.embedding IS NOT NULL AND b.embedding IS NOT NULL
              AND NOT EXISTS { (a)-[:BOUND_TO]->(:Capability)<-[:BOUND_TO]-(b) }
              AND NOT EXISTS { (a)-[:INTRODUCED_BY]-(b) }
            WITH a, b, vector.similarity.cosine(a.embedding, b.embedding) AS sim
            WHERE sim >= $threshold
            RETURN a.key AS a, b.key AS b, sim, 'unanchored-similarity' AS reason
            ORDER BY sim DESC LIMIT 50
            """,
            new { threshold = anchoredThreshold });
        var candidates = await unanchored.ToListAsync(ToCandidate);

        var siblings = await session.RunAsync(
            """
            MATCH (a:KgNode)-[:BOUND_TO]->(c:Capability)<-[:BOUND_TO]-(b:KgNode)
            WHERE (a:Feature OR a:Change) AND (b:Feature OR b:Change)
              AND a.key < b.key AND a.embedding IS NOT NULL AND b.embedding IS NOT NULL
            WITH a, b, c, vector.similarity.cosine(a.embedding, b.embedding) AS sim
            WHERE sim >= $threshold
            ORDER BY sim DESC
            WITH c, collect({ a: a.key, b: b.key, sim: sim })[0..3] AS top
            UNWIND top AS t
            RETURN t.a AS a, t.b AS b, t.sim AS sim, 'sibling-overlap:' + c.key AS reason
            ORDER BY sim DESC
            """,
            new { threshold = siblingThreshold });
        candidates.AddRange(await siblings.ToListAsync(ToCandidate));

        var docs = await session.RunAsync(
            """
            MATCH (a:Doc), (b:Doc)
            WHERE a.key < b.key AND a.embedding IS NOT NULL AND b.embedding IS NOT NULL
              AND coalesce(a.missing, false) = false AND coalesce(b.missing, false) = false
            WITH a, b, vector.similarity.cosine(a.embedding, b.embedding) AS sim
            WHERE sim >= $threshold
              AND split(a.path, '/')[0..-1] <> split(b.path, '/')[0..-1]
            RETURN a.key AS a, b.key AS b, sim, 'cross-directory-doc-twin' AS reason
            ORDER BY sim DESC LIMIT 50
            """,
            new { threshold = docThreshold });
        candidates.AddRange(await docs.ToListAsync(ToCandidate));

        return candidates;

        static DuplicateCandidate ToCandidate(IRecord r) => new(
            r["a"].As<string>(), r["b"].As<string>(), r["sim"].As<double>(), r["reason"].As<string>());
    }
}

public sealed record RetrievedNode(string Key, IReadOnlyList<string> Labels, double Score)
{
    public string? Name { get; init; }
    public string? Path { get; init; }
    public string? Status { get; init; }
    public string? Authority { get; init; }
    public string? ChangeStatus { get; init; }
    public string? Progress { get; init; }
    public int? Port { get; init; }
    public string? SourceFile { get; init; }
    public string? CommitHash { get; init; }
}

public sealed record RetrievedEdge(
    string Type, string FromKey, string ToKey, string Status,
    string? DisputeReason, string? SourceFile, string? CommitHash,
    string? Via, string? Kind, string? BridgeStatus, string? Delta, IReadOnlyList<string> Routes)
{
    public static RetrievedEdge From(IDictionary<string, object> rel) => new(
        rel["type"].ToString()!,
        rel["fromKey"].ToString()!,
        rel["toKey"].ToString()!,
        rel["status"].ToString()!,
        Get(rel, "disputeReason"),
        Get(rel, "sourceFile"),
        Get(rel, "commitHash"),
        Get(rel, "via"),
        Get(rel, "kind"),
        Get(rel, "bridgeStatus"),
        Get(rel, "delta"),
        rel.TryGetValue("routes", out var v) && v is IEnumerable<object> list
            ? list.Select(x => x?.ToString() ?? "").ToList()
            : []);

    private static string? Get(IDictionary<string, object> d, string key) =>
        d.TryGetValue(key, out var v) ? v?.ToString() : null;

    public string Citation =>
        SourceFile is null ? "" : $"{SourceFile}@{CommitHash?[..Math.Min(8, CommitHash.Length)]}";
}

public sealed record DuplicateCandidate(string A, string B, double Similarity, string Reason);

public sealed record RetrievalResult(
    string Question, QuestionKind Kind,
    IReadOnlyList<RetrievedNode> Nodes, IReadOnlyList<RetrievedEdge> Edges)
{
    public static RetrievalResult Empty(string q, QuestionKind k) => new(q, k, [], []);

    /// <summary>Compact serialization consumed by the CLI and MCP clients.</summary>
    public string Render()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {Kind} retrieval: {Question}");
        sb.AppendLine("## Nodes (ranked)");
        foreach (var n in Nodes)
        {
            var facts = new List<string>();
            if (n.Authority is not null) facts.Add($"authority={n.Authority}");
            if (n.ChangeStatus is not null) facts.Add($"change={n.ChangeStatus}");
            if (n.Progress is not null) facts.Add($"tasks={n.Progress}");
            if (n.Port is not null) facts.Add($"port={n.Port}");
            var flag = n.Status is not null and not "ACCEPTED" ? $" [{n.Status}]" : "";
            sb.AppendLine(
                $"- {n.Key} [{string.Join(",", n.Labels)}] score={n.Score:F3}" +
                (n.Name is null ? "" : $" name=\"{n.Name}\"") +
                (facts.Count > 0 ? " " + string.Join(" ", facts) : "") + flag +
                (n.SourceFile is null ? "" : $" src={n.SourceFile}@{n.CommitHash?[..Math.Min(8, n.CommitHash.Length)]}"));
        }
        sb.AppendLine("## Edges");
        foreach (var e in Edges)
        {
            var mark = e.Status == "ACCEPTED" ? "" : $" [{e.Status}{(e.DisputeReason is null ? "" : $": {e.DisputeReason}")}]";
            var bridge = e.Via is null ? "" : $" via={e.Via}({e.Kind},{e.BridgeStatus})";
            var delta = e.Delta is null ? "" : $" delta={e.Delta}";
            var routes = e.Routes.Count > 0 ? $" routes=[{string.Join(", ", e.Routes)}]" : "";
            sb.AppendLine($"- {e.FromKey} -[{e.Type}]-> {e.ToKey}{bridge}{delta}{routes}{mark} src={e.Citation}");
        }
        return sb.ToString();
    }
}
