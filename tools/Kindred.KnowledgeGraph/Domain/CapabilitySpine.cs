using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Kindred.KnowledgeGraph.Domain;

/// <summary>
/// The hand-authored capability spine — the join key across specs, changes, docs and code.
/// Capability nodes are ONLY ever created from this file; no extractor (deterministic or
/// agent) may mint one. Module bridges are declared here too, so a cross-module seam is a
/// stated fact rather than something an extractor guesses.
/// </summary>
public sealed class CapabilitySpine
{
    public required List<CapabilityDecl> Capabilities { get; init; }
    public List<BridgeDecl> Bridges { get; init; } = [];

    private Dictionary<string, CapabilityDecl>? _byId;
    public IReadOnlyDictionary<string, CapabilityDecl> ById =>
        _byId ??= Capabilities.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);

    public static CapabilitySpine Load(string yamlPath)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        var spine = deserializer.Deserialize<CapabilitySpine>(File.ReadAllText(yamlPath));
        var dupes = spine.Capabilities.GroupBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dupes.Count > 0)
            throw new InvalidOperationException($"Duplicate capability ids in spine: {string.Join(", ", dupes)}");
        return spine;
    }

    /// <summary>
    /// Deterministic binding (Tier B): exact id match, then exact alias match across ALL
    /// capabilities, then alias-prefix match (an alias ending in "*", e.g. "matching-*").
    /// Exact beats prefix everywhere; the longest matching prefix wins among prefixes.
    /// Returns null when nothing matches — the caller reports the source as UNBOUND.
    /// </summary>
    public CapabilityDecl? Bind(string sourceName)
    {
        var name = sourceName.Trim().ToLowerInvariant();

        if (ById.TryGetValue(name, out var exact)) return exact;

        foreach (var cap in Capabilities)
        {
            if (cap.Aliases.Any(a => !a.EndsWith('*') && name == a.ToLowerInvariant()))
                return cap;
        }

        CapabilityDecl? best = null;
        var bestLen = -1;
        foreach (var cap in Capabilities)
        {
            foreach (var alias in cap.Aliases.Where(a => a.EndsWith('*')))
            {
                var prefix = alias[..^1].ToLowerInvariant();
                if (name.StartsWith(prefix, StringComparison.Ordinal) && prefix.Length > bestLen)
                {
                    best = cap;
                    bestLen = prefix.Length;
                }
            }
        }

        return best;
    }
}

public sealed class CapabilityDecl
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? Module { get; init; }
    public List<string> Aliases { get; init; } = [];
}

/// <summary>A declared Module ↔ Module seam (e.g. Matching consumes Students' enrollment events).</summary>
public sealed class BridgeDecl
{
    public required string From { get; init; }
    public required string To { get; init; }
    public required string Via { get; init; }
    public string Kind { get; init; } = "domain-event";
    public string Status { get; init; } = "active";
}
