using System.Text.RegularExpressions;
using Kindred.KnowledgeGraph.Domain;

namespace Kindred.KnowledgeGraph.Infrastructure.Ingestion;

/// <summary>
/// Tier A — deterministic repo facts, no LLM anywhere:
/// csproj ProjectReferences → Service nodes + DEPENDS_ON; launchSettings ports;
/// integration events from code (declarations, publishers, consumers) → DomainEvent +
/// PUBLISHES/CONSUMED_BY; directory taxonomies (specs, changes incl. archive, docs, features).
/// Also produces the <see cref="RepoFacts"/> snapshot consumed by validation Layer 2.
/// </summary>
public sealed partial class TierAExtractor(string repoRoot, string commitHash)
{
    private const string ExtractorName = "tier-a/v1";
    private static readonly string[] CodeRoots = ["src", "tools"];

    private Provenance Prov(string sourceFile) =>
        new(RepoPath.Normalize(repoRoot, sourceFile), commitHash, Ontology.Tier.A, ExtractorName);

    public RepoFacts BuildRepoFacts(IReadOnlyCollection<string> trackedFiles)
    {
        var (_, _, depPairs) = ExtractDotnetProjects();
        var (_, eventEdges) = ExtractIntegrationEvents();
        return new RepoFacts
        {
            ExistingFiles = trackedFiles.ToHashSet(StringComparer.OrdinalIgnoreCase),
            ServiceDependencies = depPairs.ToHashSet(),
            EventLinks = eventEdges.Select(e => (e.FromKey, e.ToKey)).ToHashSet(),
        };
    }

    // ── .NET projects → Service nodes + DEPENDS_ON ──────────────────────────

    /// <summary>
    /// Service granularity: Kindred.StudentService{,.Api,.Data} collapse into one
    /// "service:student-service" node; DEPENDS_ON between services when any project of A
    /// references any project of B.
    /// </summary>
    public (List<NodeMutation> Nodes, List<EdgeMutation> Edges, List<(string, string)> DepPairs) ExtractDotnetProjects()
    {
        var nodes = new List<NodeMutation>();
        var edges = new List<EdgeMutation>();
        var depPairs = new List<(string, string)>();
        var serviceSeen = new HashSet<string>();
        var pairSeen = new HashSet<(string, string)>();

        foreach (var csproj in EnumerateProjects())
        {
            var projectName = Path.GetFileNameWithoutExtension(csproj);
            var serviceKey = ServiceKeyFor(projectName);

            if (serviceSeen.Add(serviceKey))
            {
                nodes.Add(new NodeMutation("Service", serviceKey,
                    new Dictionary<string, object?>
                    {
                        ["name"] = serviceKey["service:".Length..],
                        ["runtime"] = ".NET",
                    },
                    Prov(csproj)));
            }

            foreach (Match m in ProjectReferenceRegex().Matches(File.ReadAllText(csproj)))
            {
                var refProject = Path.GetFileNameWithoutExtension(m.Groups[1].Value.Replace('\\', '/'));
                var refKey = ServiceKeyFor(refProject);
                if (refKey == serviceKey) continue; // intra-service refs aren't service deps

                if (pairSeen.Add((serviceKey, refKey)))
                {
                    depPairs.Add((serviceKey, refKey));
                    edges.Add(new EdgeMutation("DEPENDS_ON", serviceKey, refKey,
                        new Dictionary<string, object?> { ["via"] = "ProjectReference" },
                        Prov(csproj)));
                }
            }
        }

        foreach (var (_, to) in depPairs.Where(p => serviceSeen.Add(p.Item2)))
        {
            nodes.Add(new NodeMutation("Service", to,
                new Dictionary<string, object?> { ["name"] = to["service:".Length..], ["runtime"] = ".NET" },
                Prov("Kindred.slnx")));
        }

        return (nodes, edges, depPairs);
    }

    /// <summary>Kindred.StudentService.Api → service:student-service; Kindred.Gateway → service:gateway.</summary>
    public static string ServiceKeyFor(string projectName)
    {
        var name = projectName;
        if (name.StartsWith("Kindred.", StringComparison.Ordinal)) name = name["Kindred.".Length..];
        foreach (var suffix in new[] { ".UnitTests", ".IntegrationTests", ".Tests", ".Api", ".Data" })
            if (name.EndsWith(suffix, StringComparison.Ordinal)) name = name[..^suffix.Length];

        var kebab = KebabRegex().Replace(name, "$1-$2").ToLowerInvariant().Replace('.', '-');
        return $"service:{kebab}";
    }

    /// <summary>Ports from each project's Properties/launchSettings.json (first http applicationUrl).</summary>
    public void ExtractLaunchPorts(List<NodeMutation> serviceNodes)
    {
        foreach (var settings in CodeRoots
                     .Select(d => Path.Combine(repoRoot, d))
                     .Where(Directory.Exists)
                     .SelectMany(d => Directory.EnumerateFiles(d, "launchSettings.json", SearchOption.AllDirectories))
                     .Where(NotBuildOutput))
        {
            var projectDir = Directory.GetParent(Path.GetDirectoryName(settings)!)!.FullName;
            var csproj = Directory.EnumerateFiles(projectDir, "*.csproj").FirstOrDefault();
            if (csproj is null) continue;

            var m = LaunchUrlRegex().Match(File.ReadAllText(settings));
            if (!m.Success) continue;

            var key = ServiceKeyFor(Path.GetFileNameWithoutExtension(csproj));
            var i = serviceNodes.FindIndex(n => n.Key == key);
            if (i < 0 || serviceNodes[i].Properties.ContainsKey("port")) continue;

            var props = new Dictionary<string, object?>(serviceNodes[i].Properties) { ["port"] = int.Parse(m.Groups[1].Value) };
            serviceNodes[i] = serviceNodes[i] with { Properties = props };
        }
    }

    // ── Integration events from code ─────────────────────────────────────────

    /// <summary>
    /// Deterministic event-flow facts: a type implementing IIntegrationEvent is a DomainEvent;
    /// a service whose code constructs one in a file that uses IEventBus PUBLISHES it; a service
    /// with an IIntegrationEventHandler&lt;T&gt; is who it is CONSUMED_BY. The owning service is the
    /// nearest .csproj above the file.
    /// </summary>
    public (List<NodeMutation> Nodes, List<EdgeMutation> Edges) ExtractIntegrationEvents()
    {
        var nodes = new List<NodeMutation>();
        var edges = new List<EdgeMutation>();

        var projects = EnumerateProjects()
            .Select(p => (Dir: Path.GetDirectoryName(p)! + Path.DirectorySeparatorChar,
                          Key: ServiceKeyFor(Path.GetFileNameWithoutExtension(p))))
            .OrderByDescending(p => p.Dir.Length)
            .ToList();

        var csFiles = CodeRoots
            .Select(d => Path.Combine(repoRoot, d))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(NotBuildOutput)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}tools{Path.DirectorySeparatorChar}Kindred.KnowledgeGraph"))
            .Select(f => (Path: f, Text: File.ReadAllText(f)))
            .ToList();

        var events = new Dictionary<string, string>(StringComparer.Ordinal); // name → declaring file
        foreach (var (path, text) in csFiles)
            foreach (Match m in EventDeclarationRegex().Matches(text))
                events.TryAdd(m.Groups[1].Value, path);

        foreach (var (name, file) in events)
        {
            nodes.Add(new NodeMutation("DomainEvent", $"event:{name}",
                new Dictionary<string, object?> { ["name"] = name, ["embeddingText"] = $"{name} integration event" },
                Prov(file)));
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (path, text) in csFiles)
        {
            var owner = projects.FirstOrDefault(p => path.StartsWith(p.Dir, StringComparison.Ordinal)).Key;
            if (owner is null) continue;

            if (text.Contains("IEventBus", StringComparison.Ordinal))
            {
                foreach (Match m in NewObjectRegex().Matches(text))
                {
                    var name = m.Groups[1].Value;
                    if (!events.ContainsKey(name) || events[name] == path) continue;
                    var edge = new EdgeMutation("PUBLISHES", owner, $"event:{name}",
                        new Dictionary<string, object?>(), Prov(path));
                    if (seen.Add(edge.EdgeId)) edges.Add(edge);
                }
            }

            foreach (Match m in HandlerRegex().Matches(text))
            {
                var name = m.Groups[1].Value;
                if (!events.ContainsKey(name)) continue;
                var edge = new EdgeMutation("CONSUMED_BY", $"event:{name}", owner,
                    new Dictionary<string, object?>(), Prov(path));
                if (seen.Add(edge.EdgeId)) edges.Add(edge);
            }
        }

        return (nodes, edges);
    }

    // ── Directory taxonomies ─────────────────────────────────────────────────

    public (List<NodeMutation> Nodes, List<EdgeMutation> Edges) ExtractTaxonomies()
    {
        var nodes = new List<NodeMutation>();
        var edges = new List<EdgeMutation>();

        // openspec/specs/<name>/spec.md → Spec nodes (the current-state contract).
        var specsRoot = Path.Combine(repoRoot, "openspec", "specs");
        if (Directory.Exists(specsRoot))
        {
            foreach (var dir in Directory.EnumerateDirectories(specsRoot))
            {
                var name = Path.GetFileName(dir);
                var specFile = Path.Combine(dir, "spec.md");
                var source = File.Exists(specFile) ? specFile : dir;
                nodes.Add(new NodeMutation("Spec", $"spec:{name}",
                    new Dictionary<string, object?>
                    {
                        ["name"] = name,
                        ["path"] = RepoPath.Normalize(repoRoot, source),
                        ["authority"] = "CURRENT", // specs beat docs
                    },
                    Prov(source)));
            }
        }

        // openspec/changes/<name> (active) + archive/<date>-<name> → Change nodes.
        var changesRoot = Path.Combine(repoRoot, "openspec", "changes");
        if (Directory.Exists(changesRoot))
        {
            foreach (var dir in Directory.EnumerateDirectories(changesRoot).Where(d => Path.GetFileName(d) != "archive"))
                nodes.Add(ChangeNode(dir, Path.GetFileName(dir), "active", archivedAt: null));

            var archiveRoot = Path.Combine(changesRoot, "archive");
            if (Directory.Exists(archiveRoot))
            {
                foreach (var dir in Directory.EnumerateDirectories(archiveRoot))
                {
                    var folder = Path.GetFileName(dir);
                    var m = ArchiveFolderRegex().Match(folder);
                    nodes.Add(ChangeNode(dir, m.Success ? m.Groups[2].Value : folder, "archived",
                        m.Success ? m.Groups[1].Value : null));
                }
            }
        }

        // docs/**/*.md → Doc nodes; docs/features/<name>/ → Feature nodes + DOCUMENTED_BY.
        var docsRoot = Path.Combine(repoRoot, "docs");
        if (Directory.Exists(docsRoot))
        {
            foreach (var file in Directory.EnumerateFiles(docsRoot, "*.md", SearchOption.AllDirectories))
            {
                var rel = RepoPath.Normalize(repoRoot, file);
                nodes.Add(new NodeMutation("Doc", $"doc:{rel}",
                    new Dictionary<string, object?> { ["path"] = rel, ["title"] = FirstHeading(file) },
                    Prov(file)));
            }

            var featuresRoot = Path.Combine(docsRoot, "features");
            if (Directory.Exists(featuresRoot))
            {
                foreach (var dir in Directory.EnumerateDirectories(featuresRoot))
                {
                    var name = Path.GetFileName(dir);
                    var key = $"feature:{name}";
                    nodes.Add(new NodeMutation("Feature", key,
                        new Dictionary<string, object?> { ["name"] = name },
                        Prov(dir)));

                    foreach (var file in Directory.EnumerateFiles(dir, "*.md", SearchOption.AllDirectories))
                    {
                        var rel = RepoPath.Normalize(repoRoot, file);
                        edges.Add(new EdgeMutation("DOCUMENTED_BY", key, $"doc:{rel}",
                            new Dictionary<string, object?>(), Prov(file)));
                    }
                }
            }
        }

        return (nodes, edges);
    }

    /// <summary>
    /// A Change node carries its proposal path (embedded for existence queries — in-flight work is
    /// findable from day one) and its tasks.md progress, so "what's in flight?" has an answer.
    /// </summary>
    private NodeMutation ChangeNode(string dir, string name, string changeStatus, string? archivedAt)
    {
        var props = new Dictionary<string, object?> { ["name"] = name, ["changeStatus"] = changeStatus };
        if (archivedAt is not null) props["archivedAt"] = archivedAt;

        var proposal = Path.Combine(dir, "proposal.md");
        if (File.Exists(proposal)) props["path"] = RepoPath.Normalize(repoRoot, proposal);

        var tasks = Path.Combine(dir, "tasks.md");
        if (File.Exists(tasks))
        {
            var lines = File.ReadAllLines(tasks);
            props["tasksTotal"] = lines.Count(l => TaskLineRegex().IsMatch(l));
            props["tasksDone"] = lines.Count(l => DoneTaskLineRegex().IsMatch(l));
        }

        return new NodeMutation("Change", $"change:{name}", props, Prov(dir));
    }

    private IEnumerable<string> EnumerateProjects() =>
        CodeRoots
            .Select(dir => Path.Combine(repoRoot, dir))
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.csproj", SearchOption.AllDirectories))
            .Where(NotBuildOutput);

    private static bool NotBuildOutput(string p) =>
        !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
        !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
        !p.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}");

    private static string? FirstHeading(string file)
    {
        foreach (var line in File.ReadLines(file).Take(10))
        {
            if (line.StartsWith("# ", StringComparison.Ordinal)) return line[2..].Trim();
        }
        return null;
    }

    [GeneratedRegex("""<ProjectReference\s+Include="([^"]+)""")]
    private static partial Regex ProjectReferenceRegex();

    [GeneratedRegex(@"""applicationUrl""\s*:\s*""http://localhost:(\d+)")]
    private static partial Regex LaunchUrlRegex();

    [GeneratedRegex(@"\b(?:record|class)\s+(\w+)\s*(?:\([^)]*\))?\s*:\s*[^{;]*\bIIntegrationEvent\b")]
    private static partial Regex EventDeclarationRegex();

    [GeneratedRegex(@"\bnew\s+(\w+)\s*\(")]
    private static partial Regex NewObjectRegex();

    [GeneratedRegex(@"\bIIntegrationEventHandler<\s*(\w+)\s*>")]
    private static partial Regex HandlerRegex();

    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2})-(.+)$")]
    private static partial Regex ArchiveFolderRegex();

    [GeneratedRegex(@"^\s*- \[[ xX]\]")]
    private static partial Regex TaskLineRegex();

    [GeneratedRegex(@"^\s*- \[[xX]\]")]
    private static partial Regex DoneTaskLineRegex();

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex KebabRegex();
}
