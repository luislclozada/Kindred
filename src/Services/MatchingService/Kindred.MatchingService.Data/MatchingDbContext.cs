using Kindred.MatchingService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kindred.MatchingService.Data;

public sealed class MatchingDbContext(DbContextOptions<MatchingDbContext> options) : DbContext(options)
{
    public const string DevConnectionString =
        "Host=localhost;Port=11432;Database=kindred_matching;Username=kindred;Password=kindred_dev";

    public DbSet<CourseRoster> CourseRosters => Set<CourseRoster>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MatchingDbContext).Assembly);
}

/// <summary>Lets `dotnet ef` build the context without starting the API.</summary>
public sealed class MatchingDbContextFactory : IDesignTimeDbContextFactory<MatchingDbContext>
{
    public MatchingDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<MatchingDbContext>().UseNpgsql(MatchingDbContext.DevConnectionString).Options);
}

internal sealed class CourseRosterConfiguration : IEntityTypeConfiguration<CourseRoster>
{
    public void Configure(EntityTypeBuilder<CourseRoster> builder)
    {
        builder.ToTable("course_rosters");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("CourseCode").HasMaxLength(8).ValueGeneratedNever();
        builder.Ignore(r => r.CourseCode);
        builder.Ignore(r => r.DomainEvents);

        builder.OwnsMany(r => r.Members, member =>
        {
            member.ToTable("roster_members");
            member.WithOwner().HasForeignKey("CourseCode");
            member.HasKey("CourseCode", nameof(RosterMember.StudentId));
            member.Property(m => m.StudentId).ValueGeneratedNever();
            member.Property(m => m.StudentName).HasMaxLength(120).IsRequired();
        });
        builder.Navigation(r => r.Members).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
