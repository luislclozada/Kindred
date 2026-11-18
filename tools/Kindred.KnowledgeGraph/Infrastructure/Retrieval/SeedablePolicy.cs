using System.Text;
using Kindred.KnowledgeGraph.Domain;

namespace Kindred.KnowledgeGraph.Infrastructure.Retrieval;

/// <summary>
/// Explicit, stated table of which node labels are vector-seedable and why. Whether a label is
/// seedable is a decision written down here — not a consequence of a list it was never added to.
/// <see cref="EmbeddingService"/> builds its selection clause from this table alone.
/// </summary>
public static class SeedablePolicy
{
    public sealed record Entry(string Label, string Rationale);

    public static readonly IReadOnlyList<Entry> SeedableLabels =
    [
        new("Doc", "A doc's own prose is the only honest signal of what it covers."),
        new("Spec", "A spec's requirements text is the current-state contract — the best existence signal."),
        new("Feature", "The feature README explains intent; falls back to name+title."),
        new("Change", "The proposal's Why/What makes in-flight work findable before it ships."),
        new("Capability", "Name + description: 'do we have X?' should land on the capability that owns X."),
        new("DomainEvent", "Event names are descriptive ('StudentEnrolledInCourse') — enough to seed event-flow questions."),
    ];

    // Service, Module and Decision are deliberately absent: Service nodes are matched lexically by
    // name (see HybridRetriever), Module nodes are reached via OWNED_BY/BRIDGES traversal, and
    // Decision nodes are reached from what they decided.

    static SeedablePolicy()
    {
        foreach (var entry in SeedableLabels)
            if (!Ontology.NodeLabels.Contains(entry.Label))
                throw new InvalidOperationException($"Seedable label drifted from ontology: {entry.Label}");
    }

    /// <summary>The Cypher label-disjunction clause (`n:Doc OR n:Spec OR ...`) for embedding selection.</summary>
    public static string BuildWhereClause() => string.Join(" OR ", SeedableLabels.Select(e => $"n:{e.Label}"));
}

/// <summary>Embedded/total counts for one seedable label.</summary>
public sealed record LabelCoverage(string Label, int Embedded, int Total)
{
    /// <summary>A seedable label with nodes but zero embeddings is a run failure, not a warning.</summary>
    public bool IsZeroCoverageFailure => Total > 0 && Embedded == 0;
}

public static class EmbeddingCoverage
{
    public static bool HasFailure(IEnumerable<LabelCoverage> coverage) => coverage.Any(c => c.IsZeroCoverageFailure);

    public static string Render(IEnumerable<LabelCoverage> coverage)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Embedding coverage (seedable labels)");
        foreach (var c in coverage)
        {
            var flag = c.IsZeroCoverageFailure ? "  [FAIL: zero coverage]" : "";
            sb.AppendLine($"  {c.Label,-12} {c.Embedded}/{c.Total}{flag}");
        }
        return sb.ToString();
    }
}
