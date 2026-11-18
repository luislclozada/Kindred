using System.CommandLine;
using Kindred.KnowledgeGraph.Cli;

// Local-dev .env loader: tool-local .env wins, repo-root .env is the fallback.
// NEO4J_URI / NEO4J_USER / NEO4J_PASSWORD only — no API keys of any kind.
var envFile = Path.Combine(AppContext.BaseDirectory, ".env");
if (File.Exists(envFile))
{
    DotNetEnv.Env.NoClobber().Load(envFile);
}

try
{
    var repoRoot = Kindred.KnowledgeGraph.Infrastructure.RepoRootLocator.Locate();
    var rootEnv = Path.Combine(repoRoot, ".env");
    if (File.Exists(rootEnv))
    {
        DotNetEnv.Env.NoClobber().Load(rootEnv);
    }
}
catch (InvalidOperationException)
{
    // Outside the repo — env vars must come from the host.
}

var root = new RootCommand(
    "Kindred.KnowledgeGraph — provenance-stamped knowledge graph (\"second brain\") of the Kindred repo " +
    "(ingest, validate, query, review, rollback). Advisory context, not a correctness oracle.");

root.Add(IngestCommands.Build());
root.Add(QueryCommands.Build());
root.Add(ReviewCommands.Build());
root.Add(MaintenanceCommands.Build());

var parseResult = root.Parse(args);
return await parseResult.InvokeAsync();
