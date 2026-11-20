using Kindred.StudentService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Kindred.StudentService.Data;

public sealed class StudentsDbContext(DbContextOptions<StudentsDbContext> options) : DbContext(options)
{
    public const string DevConnectionString =
        "Host=localhost;Port=11432;Database=kindred_students;Username=kindred;Password=kindred_dev";

    public DbSet<Student> Students => Set<Student>();
    public DbSet<Course> Courses => Set<Course>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StudentsDbContext).Assembly);
}

/// <summary>Lets `dotnet ef` build the context without starting the API.</summary>
public sealed class StudentsDbContextFactory : IDesignTimeDbContextFactory<StudentsDbContext>
{
    public StudentsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<StudentsDbContext>().UseNpgsql(StudentsDbContext.DevConnectionString).Options);
}
