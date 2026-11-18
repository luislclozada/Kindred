using Kindred.MatchingService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kindred.MatchingService.Data;

internal sealed class CourseRosterRepository(MatchingDbContext db) : ICourseRosterRepository
{
    public Task<CourseRoster?> GetAsync(string courseCode, CancellationToken cancellationToken) =>
        db.CourseRosters.FirstOrDefaultAsync(r => r.Id == courseCode, cancellationToken);

    public void Add(CourseRoster roster) => db.CourseRosters.Add(roster);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}

public static class DataServiceCollectionExtensions
{
    public static IServiceCollection AddMatchingData(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<MatchingDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Matching") ?? MatchingDbContext.DevConnectionString));
        services.AddScoped<ICourseRosterRepository, CourseRosterRepository>();
        return services;
    }

    /// <summary>Applies migrations when the project has any; otherwise creates the schema from the model.</summary>
    public static async Task InitializeMatchingDatabaseAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MatchingDbContext>();
        if (db.Database.GetMigrations().Any()) await db.Database.MigrateAsync();
        else await db.Database.EnsureCreatedAsync();
    }
}
