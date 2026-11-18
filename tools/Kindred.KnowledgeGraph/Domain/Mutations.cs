namespace Kindred.KnowledgeGraph.Domain;

/// <summary>
/// Mandatory origin stamp for every node and edge. <see cref="SourceFile"/> is always
/// repo-relative with forward slashes — use <see cref="RepoPath.Normalize"/> at the
/// extraction boundary.
/// </summary>
public sealed record Provenance(
    string SourceFile,
    string CommitHash,
    string ExtractorTier,
    string Extractor,
    string? SupportingQuote = null)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SourceFile)) throw new ArgumentException("Provenance.SourceFile is required");
        if (string.IsNullOrWhiteSpace(CommitHash)) throw new ArgumentException("Provenance.CommitHash is required");
        if (ExtractorTier is not (Ontology.Tier.A or Ontology.Tier.B or Ontology.Tier.C))
            throw new ArgumentException($"Provenance.ExtractorTier must be A, B, or C (got '{ExtractorTier}')");
        if (string.IsNullOrWhiteSpace(Extractor)) throw new ArgumentException("Provenance.Extractor is required");
        if (SourceFile.Contains('\\'))
            throw new ArgumentException($"Provenance.SourceFile must be normalized (forward slashes): '{SourceFile}'");
    }
}

/// <summary>A node upsert. <see cref="Key"/> is the stable identity — MERGE semantics on key.</summary>
public sealed record NodeMutation(
    string Label,
    string Key,
    IReadOnlyDictionary<string, object?> Properties,
    Provenance Provenance);

/// <summary>An edge upsert between two node keys (nodes must exist or be created in-batch).</summary>
public sealed record EdgeMutation(
    string Type,
    string FromKey,
    string ToKey,
    IReadOnlyDictionary<string, object?> Properties,
    Provenance Provenance)
{
    /// <summary>Identity for review/rollback: type + endpoints.</summary>
    public string EdgeId => $"{FromKey}-[{Type}]->{ToKey}";
}

/// <summary>A batch of mutations written (and rolled back) as a unit.</summary>
public sealed class MutationBatch
{
    public string BatchId { get; } = $"batch-{Guid.NewGuid():N}";
    public List<NodeMutation> Nodes { get; } = [];
    public List<EdgeMutation> Edges { get; } = [];

    public void Add(NodeMutation node) => Nodes.Add(node);
    public void Add(EdgeMutation edge) => Edges.Add(edge);
}

/// <summary>Per-mutation rejection produced by the validation gate.</summary>
public sealed record Rejection(string ElementDescription, string Layer, string Reason);

/// <summary>Outcome of gating + writing one batch (clean subset written, rejects reported).</summary>
public sealed record BatchResult(
    string BatchId,
    int NodesWritten,
    int EdgesWritten,
    int DisputedCount,
    int ProposedCount,
    IReadOnlyList<Rejection> Rejections);

/// <summary>
/// Snapshot of deterministic repo reality (Tier A output of the same run), used by
/// validation Layer 2 to corroborate or dispute convention/agent claims.
/// </summary>
public sealed class RepoFacts
{
    /// <summary>Repo-relative forward-slash paths of every tracked file.</summary>
    public required IReadOnlySet<string> ExistingFiles { get; init; }

    /// <summary>Service dependency pairs (fromKey, toKey) corroborated by csproj ProjectReferences.</summary>
    public required IReadOnlySet<(string From, string To)> ServiceDependencies { get; init; }

    /// <summary>Event-flow pairs corroborated by code: (service, event) publishes and (event, service) consumes.</summary>
    public IReadOnlySet<(string From, string To)> EventLinks { get; init; } = new HashSet<(string, string)>();

    public bool FileExists(string repoRelativePath) => ExistingFiles.Contains(repoRelativePath);

    public bool CorroboratesDependency(string fromKey, string toKey) =>
        ServiceDependencies.Contains((fromKey, toKey)) || EventLinks.Contains((fromKey, toKey));
}

/// <summary>
/// Provenance keys are repo-relative forward-slash paths regardless of host OS.
/// Every extractor calls this at its boundary; nothing downstream re-normalizes.
/// </summary>
public static class RepoPath
{
    public static string Normalize(string repoRoot, string absoluteOrRelativePath)
    {
        var path = absoluteOrRelativePath;
        if (Path.IsPathRooted(path))
        {
            path = Path.GetRelativePath(repoRoot, path);
        }

        path = path.Replace('\\', '/');
        while (path.StartsWith("./", StringComparison.Ordinal)) path = path[2..];

        if (path.StartsWith("../", StringComparison.Ordinal))
            throw new ArgumentException($"Path escapes the repo root: '{absoluteOrRelativePath}'");

        return path;
    }

    /// <summary>Resolve a relative markdown link against the directory of the file containing it.</summary>
    public static string ResolveLink(string repoRoot, string containingFileRepoRelative, string link)
    {
        var dir = Path.GetDirectoryName(containingFileRepoRelative.Replace('/', Path.DirectorySeparatorChar)) ?? "";
        var combined = Path.GetFullPath(Path.Combine(repoRoot, dir, link.Replace('/', Path.DirectorySeparatorChar)));
        return Normalize(repoRoot, combined);
    }
}
