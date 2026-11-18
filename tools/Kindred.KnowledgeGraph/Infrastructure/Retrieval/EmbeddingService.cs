using System.Security.Cryptography;
using System.Text;
using Neo4j.Driver;
using SmartComponents.LocalEmbeddings;

namespace Kindred.KnowledgeGraph.Infrastructure.Retrieval;

/// <summary>
/// Embeddings for every label <see cref="SeedablePolicy"/> marks seedable, stored in the Neo4j
/// native vector index and computed IN-PROCESS with a bundled ONNX model (384-dim, cosine) —
/// no embedding vendor, no API key, works offline. Content-hash cached: unchanged nodes never
/// re-embed.
/// </summary>
public sealed class EmbeddingService(KgContext ctx) : IDisposable
{
    public const string IndexName = "kg_embedding_384";
    public const int Dimensions = 384;
    // The local model attends ~512 tokens; leading content carries a document's identity.
    private const int MaxChars = 8_000;

    private readonly LocalEmbedder _embedder = new();

    public async Task EnsureVectorIndexAsync()
    {
        await using var session = ctx.Store.Driver.AsyncSession();
        await session.RunAndConsumeAsync(
            $$"""
            CREATE VECTOR INDEX {{IndexName}} IF NOT EXISTS
            FOR (n:{{Neo4jGraphStore.CommonLabel}}) ON (n.embedding)
            OPTIONS { indexConfig: {
                `vector.dimensions`: {{Dimensions}},
                `vector.similarity_function`: 'cosine'
            } }
            """);
    }

    public async Task<int> EmbedAllAsync()
    {
        await EnsureVectorIndexAsync();

        await using var session = ctx.Store.Driver.AsyncSession();
        var cursor = await session.RunAsync(
            $"""
            MATCH (n:KgNode)
            WHERE ({SeedablePolicy.BuildWhereClause()}) AND coalesce(n.missing, false) = false
            RETURN n.key AS key, n.path AS path, n.name AS name, n.title AS title,
                   n.embeddingHash AS hash, n.embeddingText AS embeddingText
            """);
        var rows = await cursor.ToListAsync(r => new
        {
            Key = r["key"].As<string>(),
            Path = r["path"].As<string?>(),
            Name = r["name"].As<string?>(),
            Title = r["title"].As<string?>(),
            Hash = r["hash"].As<string?>(),
            EmbeddingText = r["embeddingText"].As<string?>(),
        });

        var pending = new List<(string Key, string Hash, string Text)>();
        foreach (var row in rows)
        {
            var text = LoadText(row.Key, row.Path, row.Name, row.Title, row.EmbeddingText);
            if (text is null) continue;
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
            if (hash == row.Hash) continue; // unchanged → no recompute
            pending.Add((row.Key, hash, text));
        }

        var updated = 0;
        foreach (var chunk in pending.Chunk(128))
        {
            var updates = chunk.Select(c => new
            {
                key = c.Key,
                hash = c.Hash,
                embedding = _embedder.Embed(c.Text).Values.ToArray(),
            }).ToList();

            await session.RunAndConsumeAsync(
                """
                UNWIND $updates AS u
                MATCH (n:KgNode { key: u.key })
                SET n.embedding = u.embedding, n.embeddingHash = u.hash
                """,
                new { updates });
            updated += updates.Count;
        }

        return updated;
    }

    public Task<float[]> EmbedQueryAsync(string text) =>
        Task.FromResult(_embedder.Embed(text).Values.ToArray());

    /// <summary>Per-label embedded/total counts for every seedable label.</summary>
    public async Task<IReadOnlyList<LabelCoverage>> GetCoverageAsync()
    {
        await using var session = ctx.Store.Driver.AsyncSession();
        var coverage = new List<LabelCoverage>();
        foreach (var entry in SeedablePolicy.SeedableLabels)
        {
            var cursor = await session.RunAsync(
                $"""
                MATCH (n:KgNode) WHERE n:{entry.Label} AND coalesce(n.missing, false) = false
                RETURN count(n) AS total, count(n.embedding) AS embedded
                """);
            var record = await cursor.SingleAsync();
            coverage.Add(new LabelCoverage(entry.Label, record["embedded"].As<int>(), record["total"].As<int>()));
        }
        return coverage;
    }

    /// <summary>
    /// A precomputed <paramref name="embeddingText"/> wins regardless of label (Capability,
    /// DomainEvent). Otherwise a node with a <paramref name="path"/> embeds that file (Doc, Spec,
    /// Change → proposal.md), a Feature embeds its README, and everything else falls back to
    /// name + title.
    /// </summary>
    private string? LoadText(string key, string? path, string? name, string? title, string? embeddingText)
    {
        if (!string.IsNullOrWhiteSpace(embeddingText)) return Truncate(embeddingText);

        var file = path;
        if (file is null && key.StartsWith("feature:") && name is not null)
        {
            var readme = Path.Combine(ctx.RepoRoot, "docs", "features", name, "README.md");
            if (File.Exists(readme)) file = Domain.RepoPath.Normalize(ctx.RepoRoot, readme);
        }

        if (file is not null)
        {
            var full = Path.Combine(ctx.RepoRoot, file.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(full))
            {
                // Prefix the name so short files still carry their identity.
                var prefix = name is null ? "" : $"{name}\n";
                return Truncate(prefix + File.ReadAllText(full));
            }
        }

        var fallback = $"{name} {title}".Trim();
        return fallback.Length > 0 ? Truncate(fallback) : null;
    }

    private static string Truncate(string s) => s.Length > MaxChars ? s[..MaxChars] : s;

    public void Dispose() => _embedder.Dispose();
}
