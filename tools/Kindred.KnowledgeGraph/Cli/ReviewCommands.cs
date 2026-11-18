using System.CommandLine;
using Kindred.KnowledgeGraph.Infrastructure;
using Neo4j.Driver;

namespace Kindred.KnowledgeGraph.Cli;

/// <summary>
/// The human-review step (validation gate Layer 3): list PROPOSED elements,
/// approve (promote to ACCEPTED) or reject (remove via the rollback mechanism's
/// per-element equivalent). A review UI is out of scope; the CLI is the human step.
/// </summary>
internal static class ReviewCommands
{
    public static Command Build()
    {
        var review = new Command("review", "Review queue for gate-held Tier-C PROPOSED elements (CLI stub of the human step).");

        var list = new Command("list", "List PROPOSED edges awaiting review, with ids and supporting quotes.");
        list.SetAction(async (_, ct) =>
        {
            await using var ctx = KgContext.Create();
            await using var session = ctx.Store.Driver.AsyncSession();
            var cursor = await session.RunAsync(
                """
                MATCH (a:KgNode)-[r]->(b:KgNode) WHERE r.status = 'PROPOSED'
                RETURN elementId(r) AS id, a.key AS a, type(r) AS t, b.key AS b,
                       r.confidence AS confidence, r.supportingQuote AS quote, r.sourceFile AS src
                ORDER BY r.confidence DESC
                """);
            var any = false;
            await cursor.ForEachAsync(r =>
            {
                any = true;
                Console.WriteLine($"[{r["id"].As<string>()}]");
                Console.WriteLine($"  {r["a"].As<string>()} -[{r["t"].As<string>()}]-> {r["b"].As<string>()}" +
                                  $"  conf={r["confidence"].As<double?>():F2}  src={r["src"].As<string?>()}");
                var quote = r["quote"].As<string?>();
                if (quote is not null) Console.WriteLine($"  \"{quote}\"");
            });
            if (!any) Console.WriteLine("review queue is empty");
        });
        review.Add(list);

        var approveId = new Option<string>("--id") { Description = "Element id from 'review list'.", Required = true };
        var approve = new Command("approve", "Promote a PROPOSED edge to ACCEPTED (joins default query results).");
        approve.Add(approveId);
        approve.SetAction(async (parseResult, ct) =>
        {
            await using var ctx = KgContext.Create();
            await using var session = ctx.Store.Driver.AsyncSession();
            var cursor = await session.RunAsync(
                """
                MATCH (:KgNode)-[r]->(:KgNode) WHERE elementId(r) = $id AND r.status = 'PROPOSED'
                SET r.status = 'ACCEPTED', r.reviewedAt = datetime()
                RETURN count(r) AS c
                """,
                new { id = parseResult.GetValue(approveId) });
            var count = (await cursor.SingleAsync())["c"].As<int>();
            Console.WriteLine(count == 1 ? "approved" : "no PROPOSED edge with that id");
        });
        review.Add(approve);

        var rejectId = new Option<string>("--id") { Description = "Element id from 'review list'.", Required = true };
        var reject = new Command("reject", "Remove a PROPOSED edge from the graph.");
        reject.Add(rejectId);
        reject.SetAction(async (parseResult, ct) =>
        {
            await using var ctx = KgContext.Create();
            await using var session = ctx.Store.Driver.AsyncSession();
            var cursor = await session.RunAsync(
                """
                MATCH (:KgNode)-[r]->(:KgNode) WHERE elementId(r) = $id AND r.status = 'PROPOSED'
                DELETE r
                RETURN count(r) AS c
                """,
                new { id = parseResult.GetValue(rejectId) });
            var count = (await cursor.SingleAsync())["c"].As<int>();
            Console.WriteLine(count == 1 ? "rejected and removed" : "no PROPOSED edge with that id");
        });
        review.Add(reject);

        return review;
    }
}
