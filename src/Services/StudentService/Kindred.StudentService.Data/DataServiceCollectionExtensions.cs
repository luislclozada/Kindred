using Kindred.StudentService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kindred.StudentService.Data;

public static class DataServiceCollectionExtensions
{
    public static IServiceCollection AddStudentsData(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<StudentsDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Students") ?? StudentsDbContext.DevConnectionString));
        services.AddScoped<IStudentRepository, StudentRepository>();
        services.AddScoped<ICourseRepository, CourseRepository>();
        return services;
    }

    /// <summary>Applies migrations when the project has any; otherwise creates the schema from the model.</summary>
    public static async Task InitializeStudentsDatabaseAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        if (db.Database.GetMigrations().Any()) await db.Database.MigrateAsync();
        else await db.Database.EnsureCreatedAsync();
    }
}
