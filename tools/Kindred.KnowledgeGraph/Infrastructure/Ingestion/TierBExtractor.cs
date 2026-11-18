using System.Text.Json;
using System.Text.RegularExpressions;
using Kindred.KnowledgeGraph.Domain;

namespace Kindred.KnowledgeGraph.Infrastructure.Ingestion;

/// <summary>
/// Tier B — convention-grammar parsing, no LLM: the capability spine (+ declared bridges),
/// FEATURES.md entry grammar, DEPRECATED markers, relative markdown links, openspec
/// requirement blocks, in-flight change deltas, gateway routes, and the spine binder.
/// </summary>
public sealed partial class TierBExtractor(string repoRoot, string commitHash, CapabilitySpine spine)
{
    private const string ExtractorName = "tier-b/v1";
    public const string GatewayExtractorName = "tier-b-gateway-routes/v1";
    public const string GatewayConfigPath = "src/Gateway/Kindred.Gateway/appsettings.json";

    private Provenance Prov(string sourceFile, string? quote = null, string extractor = ExtractorName) =>
        new(RepoPath.Normalize(repoRoot, sourceFile), commitHash, Ontology.Tier.B, extractor, quote);

    // ── Spine: Capability + Module nodes + declared bridges (the only Capability mint) ──

    public (List<NodeMutation> Nodes, List<EdgeMutation> Edges) ExtractSpine(string spineFile)
    {
        var nodes = new List<NodeMutation>();
        var edges = new List<EdgeMutation>();
        var spineProv = new Provenance(
            RepoPath.Normalize(repoRoot, spineFile), commitHash, Ontology.Tier.B, ValidationGate.SpineExtractor);

        var modules = new HashSet<string>();
        void EnsureModule(string module)
        {
            if (modules.Add(module))
                nodes.Add(new NodeMutation("Module", $"module:{module}",
                    new Dictionary<string, object?> { ["name"] = module }, spineProv));
        }

        foreach (var cap in spine.Capabilities)
        {
            nodes.Add(new NodeMutation("Capability", $"capability:{cap.Id}",
                new Dictionary<string, object?>
                {
                    ["name"] = cap.Name,
                    ["module"] = cap.Module,
                    ["description"] = cap.Description,
                    // Capabilities are vector-seedable: "do we have matching?" should land on one.
                    ["embeddingText"] = $"{cap.Name}. {cap.Description}".Trim(),
                },
                spineProv));

            if (cap.Module is not null)
            {
                EnsureModule(cap.Module);
                edges.Add(new EdgeMutation("OWNED_BY", $"capability:{cap.Id}", $"module:{cap.Module}",
                    new Dictionary<string, object?>(), spineProv));
            }
        }

        foreach (var bridge in spine.Bridges)
        {
            EnsureModule(bridge.From);
            EnsureModule(bridge.To);
            edges.Add(new EdgeMutation("BRIDGES", $"module:{bridge.From}", $"module:{bridge.To}",
                new Dictionary<string, object?>
                {
                    ["via"] = bridge.Via,
                    ["kind"] = bridge.Kind,
                    ["bridgeStatus"] = bridge.Status,
                },
                spineProv));
        }

        return (nodes, edges);
    }

    // ── Spine binder: BOUND_TO edges for name-identified sources ────────────

    public (List<EdgeMutation> Edges, Dictionary<string, List<string>> Unbound) BindToSpine(
        IEnumerable<NodeMutation> nodes)
    {
        var edges = new List<EdgeMutation>();
        var unbound = new Dictionary<string, List<string>>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in nodes)
        {
            if (node.Label is not ("Spec" or "Feature" or "Change" or "Service")) continue;
            if (!seen.Add(node.Key)) continue;
            var name = node.Properties.TryGetValue("name", out var n) ? n as string : null;
            if (name is null) continue;

            var cap = spine.Bind(name);
            if (cap is null)
            {
                if (!unbound.TryGetValue(node.Label, out var list)) unbound[node.Label] = list = [];
                list.Add(name);
                continue;
            }

            edges.Add(new EdgeMutation("BOUND_TO", node.Key, $"capability:{cap.Id}",
                new Dictionary<string, object?> { ["match"] = "spine-alias" },
                Prov("knowledge-graph/capabilities.yaml")));
        }

        return (edges, unbound);
    }

    // ── FEATURES.md entry grammar ────────────────────────────────────────────
    //   - [Title](features/<name>/) (YYYY-MM-DD) — prose…            → shippedAt, DOCUMENTED_BY
    //   - ~~[Title](…)~~ … DEPRECATED … superseded by <Other Title>  → status + SUPERSEDED_BY

    public (List<NodeMutation> Nodes, List<EdgeMutation> Edges) ExtractFeaturesIndex()
    {
        var nodes = new List<NodeMutation>();
        var edges = new List<EdgeMutation>();
        var file = Path.Combine(repoRoot, "docs", "FEATURES.md");
        if (!File.Exists(file)) return (nodes, edges);

        var rel = RepoPath.Normalize(repoRoot, file);
        var titleToKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<(string Key, string Name, string Title, string Line)>();

        foreach (var line in File.ReadLines(file))
        {
            var m = FeatureEntryRegex().Match(line);
            if (!m.Success) continue;
            var folderMatch = FeatureFolderRegex().Match(m.Groups["link"].Value);
            if (!folderMatch.Success) continue;

            var title = m.Groups["title"].Value.Trim();
            var name = folderMatch.Groups[1].Value;
            var key = $"feature:{name}";
            titleToKey[title] = key;
            entries.Add((key, name, title, line));
        }

        foreach (var (key, name, title, line) in entries)
        {
            var props = new Dictionary<string, object?> { ["name"] = name, ["title"] = title };
            var date = ShippedDateRegex().Match(line);
            if (date.Success) props["shippedAt"] = date.Groups[1].Value;
            if (line.Contains("~~[", StringComparison.Ordinal) || line.Contains("DEPRECATED", StringComparison.Ordinal))
                props["status"] = "deprecated";

            nodes.Add(new NodeMutation("Feature", key, props, Prov(rel, Truncate(line))));
            edges.Add(new EdgeMutation("DOCUMENTED_BY", key, $"doc:{rel}",
                new Dictionary<string, object?>(), Prov(rel)));

            var superseded = SupersededByRegex().Match(line);
            if (!superseded.Success) continue;
            var successorTitle = superseded.Groups[1].Value.Trim().TrimEnd('.');
            var successor = titleToKey.Keys.FirstOrDefault(t =>
                successorTitle.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                t.Contains(successorTitle, StringComparison.OrdinalIgnoreCase));
            if (successor is not null && titleToKey[successor] != key)
                edges.Add(new EdgeMutation("SUPERSEDED_BY", key, titleToKey[successor],
                    new Dictionary<string, object?>(), Prov(rel, Truncate(line))));
        }

        return (nodes, edges);
    }

    // ── Relative markdown links across docs/ → REFERENCES ───────────────────

    /// <summary>
    /// Dead targets still produce edges: a placeholder Doc node (missing=true) is created so
    /// the claim exists in the graph, and Layer 2 marks the edge DISPUTED:target-missing.
    /// </summary>
    public (List<NodeMutation> Nodes, List<EdgeMutation> Edges) ExtractMarkdownLinks(
        RepoFacts facts, IEnumerable<string>? scopeFiles = null)
    {
        var nodes = new List<NodeMutation>();
        var edges = new List<EdgeMutation>();
        var placeholders = new HashSet<string>();

        var docsRoot = Path.Combine(repoRoot, "docs");
        if (!Directory.Exists(docsRoot)) return (nodes, edges);

        var files = scopeFiles?.Select(f => Path.Combine(repoRoot, f.Replace('/', Path.DirectorySeparatorChar)))
                    ?? Directory.EnumerateFiles(docsRoot, "*.md", SearchOption.AllDirectories);

        foreach (var file in files)
        {
            if (!File.Exists(file)) continue;
            var fromRel = RepoPath.Normalize(repoRoot, file);

            foreach (Match m in MarkdownLinkRegex().Matches(File.ReadAllText(file)))
            {
                var link = m.Groups[1].Value.Split('#')[0].Trim();
                if (link.Length == 0 || ExternalLinkRegex().IsMatch(link)) continue;

                string targetRel;
                try { targetRel = RepoPath.ResolveLink(repoRoot, fromRel, link); }
                catch (ArgumentException) { continue; }

                var exists = facts.FileExists(targetRel);
                if (!exists && facts.FileExists($"{targetRel.TrimEnd('/')}/README.md"))
                {
                    targetRel = $"{targetRel.TrimEnd('/')}/README.md";
                    exists = true;
                }

                var targetKey = $"doc:{targetRel}";
                if ((!exists || !targetRel.StartsWith("docs/", StringComparison.Ordinal)) && placeholders.Add(targetKey))
                {
                    var props = new Dictionary<string, object?> { ["path"] = targetRel };
                    if (!exists) props["missing"] = true;
                    nodes.Add(new NodeMutation("Doc", targetKey, props, Prov(fromRel)));
                }

                edges.Add(new EdgeMutation("REFERENCES", $"doc:{fromRel}", targetKey,
                    new Dictionary<string, object?>(), Prov(fromRel, Truncate(m.Value))));
            }
        }

        return (nodes, edges);
    }

    // ── openspec requirement blocks → summaries on Spec nodes ───────────────

    public List<NodeMutation> ExtractSpecRequirements()
    {
        var nodes = new List<NodeMutation>();
        var specsRoot = Path.Combine(repoRoot, "openspec", "specs");
        if (!Directory.Exists(specsRoot)) return nodes;

        foreach (var dir in Directory.EnumerateDirectories(specsRoot))
        {
            var specFile = Path.Combine(dir, "spec.md");
            if (!File.Exists(specFile)) continue;
            var requirements = Requirements(specFile);
            if (requirements.Count == 0) continue;

            nodes.Add(new NodeMutation("Spec", $"spec:{Path.GetFileName(dir)}",
                new Dictionary<string, object?> { ["requirements"] = requirements },
                Prov(specFile)));
        }

        return nodes;
    }

    // ── In-flight change deltas → INTRODUCED_BY (+ IN_FLIGHT Spec for brand-new capabilities) ──

    /// <summary>
    /// openspec/changes/&lt;change&gt;/specs/&lt;spec&gt;/spec.md is a delta. If &lt;spec&gt; already exists,
    /// the change modifies it: spec -[INTRODUCED_BY {delta: MODIFIED}]-> change, which puts the
    /// change inside that spec's blast radius. If it doesn't exist yet, the change introduces a
    /// new spec: an IN_FLIGHT Spec node is minted from the delta so existence queries find the
    /// work before it ships. Archiving replaces it with the CURRENT spec under the same key.
    /// </summary>
    public (List<NodeMutation> Nodes, List<EdgeMutation> Edges) ExtractChangeDeltas()
    {
        var nodes = new List<NodeMutation>();
        var edges = new List<EdgeMutation>();
        var changesRoot = Path.Combine(repoRoot, "openspec", "changes");
        if (!Directory.Exists(changesRoot)) return (nodes, edges);

        var specsRoot = Path.Combine(repoRoot, "openspec", "specs");

        foreach (var changeDir in Directory.EnumerateDirectories(changesRoot).Where(d => Path.GetFileName(d) != "archive"))
        {
            var deltaRoot = Path.Combine(changeDir, "specs");
            if (!Directory.Exists(deltaRoot)) continue;
            var change = Path.GetFileName(changeDir);

            foreach (var deltaDir in Directory.EnumerateDirectories(deltaRoot))
            {
                var spec = Path.GetFileName(deltaDir);
                var deltaFile = Path.Combine(deltaDir, "spec.md");
                if (!File.Exists(deltaFile)) continue;

                var existsAlready = Directory.Exists(Path.Combine(specsRoot, spec));
                if (!existsAlready)
                {
                    nodes.Add(new NodeMutation("Spec", $"spec:{spec}",
                        new Dictionary<string, object?>
                        {
                            ["name"] = spec,
                            ["path"] = RepoPath.Normalize(repoRoot, deltaFile),
                            ["authority"] = "IN_FLIGHT",
                            ["requirements"] = Requirements(deltaFile),
                        },
                        Prov(deltaFile)));
                }

                edges.Add(new EdgeMutation("INTRODUCED_BY", $"spec:{spec}", $"change:{change}",
                    new Dictionary<string, object?> { ["delta"] = existsAlready ? "MODIFIED" : "ADDED" },
                    Prov(deltaFile)));
            }
        }

        return (nodes, edges);
    }

    // ── Gateway routes (YARP config) → CALLS ────────────────────────────────

    /// <summary>
    /// The gateway's ReverseProxy config is the complete inventory of what it forwards where:
    /// one CALLS edge per cluster, carrying the route paths. A cluster id is the target
    /// service's kebab name (student-service, matching-service). The extractor owns these edges
    /// outright — the pipeline retracts any it did not re-emit.
    /// </summary>
    public List<EdgeMutation> ExtractGatewayRoutes(IReadOnlySet<string> knownServiceKeys)
    {
        var edges = new List<EdgeMutation>();
        var config = Path.Combine(repoRoot, GatewayConfigPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(config) || !knownServiceKeys.Contains("service:gateway")) return edges;

        using var doc = JsonDocument.Parse(File.ReadAllText(config),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (!doc.RootElement.TryGetProperty("ReverseProxy", out var proxy) ||
            !proxy.TryGetProperty("Routes", out var routes)) return edges;

        var byCluster = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var route in routes.EnumerateObject())
        {
            if (!route.Value.TryGetProperty("ClusterId", out var cluster)) continue;
            var path = route.Value.TryGetProperty("Match", out var match) && match.TryGetProperty("Path", out var p)
                ? p.GetString() ?? "" : "";
            var id = cluster.GetString() ?? "";
            if (!byCluster.TryGetValue(id, out var list)) byCluster[id] = list = [];
            list.Add(path);
        }

        foreach (var (cluster, paths) in byCluster)
        {
            var target = $"service:{cluster}";
            if (!knownServiceKeys.Contains(target)) continue; // unknown cluster: not a claim we can anchor
            edges.Add(new EdgeMutation("CALLS", "service:gateway", target,
                new Dictionary<string, object?> { ["routes"] = paths, ["routeCount"] = paths.Count },
                Prov(GatewayConfigPath, extractor: GatewayExtractorName)));
        }

        return edges;
    }

    private static List<string> Requirements(string specFile) =>
        File.ReadLines(specFile)
            .Select(l => RequirementHeaderRegex().Match(l))
            .Where(m => m.Success)
            .Select(m => m.Groups[1].Value.Trim())
            .ToList();

    private static string Truncate(string s) => s.Length <= 200 ? s : s[..200];

    [GeneratedRegex(@"^- (?:~~)?\[(?<title>[^\]]+)\]\((?<link>[^)]+)\)")]
    private static partial Regex FeatureEntryRegex();

    [GeneratedRegex(@"features/([a-z0-9-]+)/?")]
    private static partial Regex FeatureFolderRegex();

    [GeneratedRegex(@"\((\d{4}-\d{2}-\d{2})\)")]
    private static partial Regex ShippedDateRegex();

    [GeneratedRegex(@"superseded by ([^;.,]+)", RegexOptions.IgnoreCase)]
    private static partial Regex SupersededByRegex();

    [GeneratedRegex(@"\]\(([^)]+)\)")]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex(@"^(https?:|mailto:|#)")]
    private static partial Regex ExternalLinkRegex();

    [GeneratedRegex(@"^### Requirement:\s*(.+)$")]
    private static partial Regex RequirementHeaderRegex();
}
