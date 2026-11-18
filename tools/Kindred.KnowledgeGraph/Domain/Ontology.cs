namespace Kindred.KnowledgeGraph.Domain;

/// <summary>
/// The closed graph ontology. Any write using a label or relationship type outside these
/// sets is rejected by the structural validation layer. `graph schema` generates
/// knowledge-graph/schema-reference.md from this file — it is the single source of truth.
/// </summary>
public static class Ontology
{
    public static readonly IReadOnlySet<string> NodeLabels = new HashSet<string>(StringComparer.Ordinal)
    {
        "Capability",   // capability:<id> — minted ONLY from knowledge-graph/capabilities.yaml
        "Module",       // module:<Name> — the bounded context a capability belongs to
        "Feature",      // feature:<name> — docs/features/<name>/
        "Service",      // service:<kebab-name> — a .NET deployable (Kindred.StudentService{,.Api,.Data} → service:student-service)
        "Spec",         // spec:<name> — openspec/specs/<name>/spec.md (authority CURRENT) or an in-flight delta (IN_FLIGHT)
        "Change",       // change:<name> — openspec/changes/<name> (active) or archive/<date>-<name>
        "Doc",          // doc:<repo/relative/path.md>
        "Decision",     // decision:<change>/<id>
        "DomainEvent",  // event:<EventName> — integration events crossing service boundaries
    };

    public static readonly IReadOnlySet<string> EdgeTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "BOUND_TO",       // source artifact → Capability (spine join)
        "OWNED_BY",       // Capability → Module, Feature/Spec → Service
        "DEPENDS_ON",     // Service → Service (csproj ProjectReference), Feature → Feature/Service
        "BRIDGES",        // Module ↔ Module (props: via, kind, bridgeStatus) — declared in the spine
        "SUPERSEDED_BY",  // Feature/Change → its replacement
        "DOCUMENTED_BY",  // Feature/Service/Capability → Doc
        "REFERENCES",     // Doc → Doc (relative markdown links)
        "SPECIFIED_BY",   // Capability/Feature → Spec
        "INTRODUCED_BY",  // Spec → Change (an in-flight change carries a delta for this spec)
        "DECIDED_BY",     // Feature/edge subject → Decision
        "PUBLISHES",      // Service → DomainEvent (from code: IEventBus.PublishAsync(new XEvent ...))
        "CONSUMED_BY",    // DomainEvent → Service (from code: IIntegrationEventHandler<XEvent>)
        "DUPLICATES",     // Feature/Spec/Doc → Feature/Spec/Doc (Tier C candidates, never auto-accepted)
        "CALLS",          // Service → Service over HTTP (gateway routes from the YARP config; props: routes)
    };

    /// <summary>Key prefix → node label, for endpoints that carry no label of their own in a batch.</summary>
    private static readonly IReadOnlyDictionary<string, string> KeyPrefixToLabel =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["capability:"] = "Capability",
            ["module:"] = "Module",
            ["feature:"] = "Feature",
            ["service:"] = "Service",
            ["spec:"] = "Spec",
            ["change:"] = "Change",
            ["doc:"] = "Doc",
            ["decision:"] = "Decision",
            ["event:"] = "DomainEvent",
        };

    /// <summary>Best-effort label inference from a node key's prefix. Null when the prefix is unknown.</summary>
    public static string? LabelForKey(string key)
    {
        foreach (var (prefix, label) in KeyPrefixToLabel)
            if (key.StartsWith(prefix, StringComparison.Ordinal))
                return label;
        return null;
    }

    /// <summary>Provenance properties required on every node and every relationship.</summary>
    public static readonly IReadOnlyList<string> RequiredProvenanceProps =
    [
        "sourceFile", "commitHash", "extractorTier", "batchId", "extractedAt", "extractor",
    ];

    /// <summary>Element statuses. Accepted is the default; the others gate visibility.</summary>
    public static class Status
    {
        public const string Accepted = "ACCEPTED";
        public const string Proposed = "PROPOSED";   // Tier C, awaiting review
        public const string Disputed = "DISPUTED";   // contradicted / uncorroborated by repo facts
    }

    public static class Tier
    {
        public const string A = "A"; // deterministic repo facts
        public const string B = "B"; // convention-grammar parsing
        public const string C = "C"; // agent/LLM extraction — always gate-held as PROPOSED
    }
}
