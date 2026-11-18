namespace Kindred.KnowledgeGraph.Domain;

/// <summary>
/// The 3-layer pre-write validation gate.
/// Layer 1: structural conformance against the closed ontology + provenance.
/// Layer 2: consistency cross-check against <see cref="RepoFacts"/> — contradicted or
///          uncorroborated claims are written as DISPUTED, never silently accepted or dropped.
/// Layer 3: risk routing by extractor tier — Tier C survivors become PROPOSED.
/// Failures are per-mutation: a batch writes its clean subset and reports rejects.
/// </summary>
public sealed class ValidationGate(RepoFacts facts, CapabilitySpine spine)
{
    /// <summary>Extractor name the spine loader stamps — the only source allowed to mint Capability nodes.</summary>
    public const string SpineExtractor = "spine-loader";

    public GatedBatch Run(MutationBatch batch, IReadOnlySet<string> existingNodeKeys)
    {
        var rejections = new List<Rejection>();
        var nodes = new List<NodeMutation>();
        var edges = new List<EdgeMutation>();
        var disputed = 0;
        var proposed = 0;

        var inBatchKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in batch.Nodes)
        {
            var rej = CheckNodeLayer1(node);
            if (rej is not null) { rejections.Add(rej); continue; }

            var (routed, wasProposed) = RouteNode(node);
            if (wasProposed) proposed++;
            nodes.Add(routed);
            inBatchKeys.Add(node.Key);
        }

        // Tier A same-run facts: a lower-tier duplicate claim is corroboration, not a second edge.
        var tierAEdges = batch.Edges
            .Where(e => e.Provenance.ExtractorTier == Ontology.Tier.A)
            .Select(e => e.EdgeId)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var edge in batch.Edges)
        {
            var rej = CheckEdgeLayer1(edge, existingNodeKeys, inBatchKeys);
            if (rej is not null) { rejections.Add(rej); continue; }

            if (edge.Provenance.ExtractorTier != Ontology.Tier.A && tierAEdges.Contains(edge.EdgeId))
                continue;

            var (routedEdge, status) = RouteEdge(edge);
            if (status == Ontology.Status.Disputed) disputed++;
            if (status == Ontology.Status.Proposed) proposed++;
            edges.Add(routedEdge);
        }

        return new GatedBatch(batch.BatchId, nodes, edges, rejections, disputed, proposed);
    }

    // ── Layer 1: structural ──────────────────────────────────────────────────

    private Rejection? CheckNodeLayer1(NodeMutation node)
    {
        if (!Ontology.NodeLabels.Contains(node.Label))
            return new Rejection($"node {node.Key}", "structural", $"unknown-label:{node.Label}");

        var prov = TryValidateProvenance(node.Provenance, $"node {node.Key}");
        if (prov is not null) return prov;

        // Capability nodes come ONLY from the spine — an agent cannot mint capabilities.
        if (node.Label == "Capability")
        {
            if (node.Provenance.Extractor != SpineExtractor)
                return new Rejection($"node {node.Key}", "structural", "capability-not-from-spine");
            var id = node.Key.StartsWith("capability:") ? node.Key["capability:".Length..] : node.Key;
            if (!spine.ById.ContainsKey(id))
                return new Rejection($"node {node.Key}", "structural", $"unknown-capability:{id}");
        }

        return null;
    }

    private Rejection? CheckEdgeLayer1(EdgeMutation edge, IReadOnlySet<string> existing, IReadOnlySet<string> inBatch)
    {
        if (!Ontology.EdgeTypes.Contains(edge.Type))
            return new Rejection(edge.EdgeId, "structural", $"unknown-edge-type:{edge.Type}");

        var prov = TryValidateProvenance(edge.Provenance, edge.EdgeId);
        if (prov is not null) return prov;

        // BOUND_TO must target a spine capability — checked before generic endpoint existence
        // so an unknown capability id gets its specific reason.
        if (edge.Type == "BOUND_TO")
        {
            var id = edge.ToKey.StartsWith("capability:") ? edge.ToKey["capability:".Length..] : edge.ToKey;
            if (!spine.ById.ContainsKey(id))
                return new Rejection(edge.EdgeId, "structural", $"unknown-capability:{id}");
        }

        if (!existing.Contains(edge.FromKey) && !inBatch.Contains(edge.FromKey))
            return new Rejection(edge.EdgeId, "structural", $"endpoint-missing:{edge.FromKey}");
        if (!existing.Contains(edge.ToKey) && !inBatch.Contains(edge.ToKey))
            return new Rejection(edge.EdgeId, "structural", $"endpoint-missing:{edge.ToKey}");

        return null;
    }

    private static Rejection? TryValidateProvenance(Provenance p, string element)
    {
        try { p.Validate(); return null; }
        catch (ArgumentException ex) { return new Rejection(element, "structural", $"missing-provenance:{ex.Message}"); }
    }

    // ── Layers 2 + 3: consistency + risk routing ─────────────────────────────

    private static (NodeMutation Node, bool Proposed) RouteNode(NodeMutation node)
    {
        var props = new Dictionary<string, object?>(node.Properties);

        if (node.Provenance.ExtractorTier == Ontology.Tier.C)
        {
            props["status"] = Ontology.Status.Proposed;
            return (node with { Properties = props }, true);
        }

        props.TryAdd("status", Ontology.Status.Accepted);
        return (node with { Properties = props }, false);
    }

    private (EdgeMutation Edge, string Status) RouteEdge(EdgeMutation edge)
    {
        var props = new Dictionary<string, object?>(edge.Properties);
        string status = Ontology.Status.Accepted;

        // Layer 2a: link-shaped claims must point at files that exist.
        if (edge.Type is "DOCUMENTED_BY" or "REFERENCES")
        {
            var target = KeyToPath(edge.ToKey);
            if (target is not null && !facts.FileExists(target))
            {
                status = Ontology.Status.Disputed;
                props["disputeReason"] = "target-missing";
            }
        }

        // Layer 2b: a doc- or agent-claimed dependency between services (or a claimed event
        // flow) needs a corroborating repo fact. A poisoned doc cannot silently enter as truth.
        if (edge.Type is "DEPENDS_ON" or "PUBLISHES" or "CONSUMED_BY"
            && edge.Provenance.ExtractorTier != Ontology.Tier.A
            && IsCorroborableEndpoint(edge.FromKey) && IsCorroborableEndpoint(edge.ToKey)
            && !facts.CorroboratesDependency(edge.FromKey, edge.ToKey))
        {
            status = Ontology.Status.Disputed;
            props["disputeReason"] = "uncorroborated-by-repo-facts";
        }

        // Layer 3: Tier C survivors are held as PROPOSED (dispute outranks proposal).
        if (status == Ontology.Status.Accepted && edge.Provenance.ExtractorTier == Ontology.Tier.C)
            status = Ontology.Status.Proposed;

        props["status"] = status;
        return (edge with { Properties = props }, status);
    }

    /// <summary>Doc keys embed their repo path ("doc:docs/x.md"); other key kinds are name-identified.</summary>
    private static string? KeyToPath(string key) =>
        key.StartsWith("doc:", StringComparison.Ordinal) ? key["doc:".Length..] : null;

    private static bool IsCorroborableEndpoint(string key) =>
        key.StartsWith("service:", StringComparison.Ordinal) ||
        key.StartsWith("event:", StringComparison.Ordinal);
}

/// <summary>Gate output: the writable clean subset plus the reject report.</summary>
public sealed record GatedBatch(
    string BatchId,
    IReadOnlyList<NodeMutation> Nodes,
    IReadOnlyList<EdgeMutation> Edges,
    IReadOnlyList<Rejection> Rejections,
    int DisputedCount,
    int ProposedCount);
